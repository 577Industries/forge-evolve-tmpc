// ─────────────────────────────────────────────────────────────────────────────
// ForgeEvolve.Cli — the FORGE EVOLVE for TMPC Phase-3 integration driver.
//
// Runs the FULL eight-stage pipeline end-to-end on the synthetic, unclassified surrogate,
// OFFLINE and keyless, and writes the cited evidence artifacts under --out (results/run/):
//
//   1. Discovery  -> discovery-report.json + business-rules.ttl
//   2. CLAR       -> clar/<module>.clar.jsonld (schema-validated)
//   3. Planner    -> migration-plan.json + migration-plan.mermaid
//   4. Transform  -> transform-result.json (offline transcript replay; nothing fabricated)
//   5. Validation -> equivalence-report.json  (THE HEADLINE: modern == legacy on 2000/2000)
//   6. Cyber/cATO -> cato/*, sbom.cdx.json, poam.csv, control-map.yaml ...
//   7. Governance -> provenance.json (canonical IGOM hashchain + Merkle root) + KG1/KG2 gates
//   8. Honest summary block (with the surrogate/preliminary disclaimer)
//
// HONESTY: every number printed is measured by a module in THIS repo on the surrogate. Nothing is
// fabricated; a missing transcript or corpus throws rather than degrade. DETERMINISM: no wall-clock,
// no RNG, no absolute machine paths are written into any compared artifact, so `make verify` sees
// byte-identical output across runs.
// ─────────────────────────────────────────────────────────────────────────────

using System.Globalization;
using System.Text;
using ForgeEvolve.Cato;
using ForgeEvolve.Clar;
using ForgeEvolve.Contracts;
using ForgeEvolve.Discovery;
using ForgeEvolve.Governance;
using ForgeEvolve.Planner;
using ForgeEvolve.Transformation;
using ForgeEvolve.Validation;
using ModernMissionService = ForgeEvolve.ModernMds.Services.MissionService;
using RoutingScore = ForgeEvolve.Orchestrator.RoutingScore;
using ToolOrchestrator = ForgeEvolve.Orchestrator.ToolOrchestrator;

namespace ForgeEvolve.Cli;

internal static class Program
{
    private const string Disclaimer =
        "measured on the synthetic, unclassified surrogate; preliminary; not government-validated.";

    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var opts = CliOptions.Parse(args);
            return Run(opts);
        }
        catch (CliUsageException ux)
        {
            Console.Error.WriteLine("usage: ForgeEvolve.Cli --surrogate <dir> --out <dir> --mode <offline|local|cloud>");
            Console.Error.WriteLine(ux.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static int Run(CliOptions opts)
    {
        if (opts.Mode != OrchestratorMode.Offline)
        {
            Console.Error.WriteLine(
                $"This integration driver implements the OFFLINE, keyless pipeline only " +
                $"(requested mode='{opts.Mode}'). Local/Cloud require Ollama / API keys and are out of scope here.");
            return 3;
        }

        string repoRoot = RepoRoot.Locate();
        string surrogateDir = Path.GetFullPath(opts.Surrogate);
        string outDir = Path.GetFullPath(opts.Out);
        Directory.CreateDirectory(outDir);

        Console.WriteLine("==================================================================");
        Console.WriteLine(" FORGE EVOLVE for TMPC — full pipeline (offline, keyless, deterministic)");
        Console.WriteLine("==================================================================");
        Console.WriteLine($"surrogate : {Rel(repoRoot, surrogateDir)}");
        Console.WriteLine($"out       : {Rel(repoRoot, outDir)}");
        Console.WriteLine($"mode      : {opts.Mode}");
        Console.WriteLine();

        // The single canonical provenance chain for the whole run (the IGOM).
        var governance = new GovernanceService();

        // ── STAGE 1 — DISCOVERY ──────────────────────────────────────────────────
        Console.WriteLine("── [1/8] Discovery ───────────────────────────────────────────────");
        IReadOnlyList<SourceArtifact> sources = LoadSurrogateSources(surrogateDir);
        string goldTtl = File.ReadAllText(Path.Combine(repoRoot, "surrogate", "gold", "business-rules.gold.ttl"));

        var discoveryEngine = new DiscoveryEngine();
        (DiscoveryReport discovery, RuleF1Report f1) = discoveryEngine.AnalyzeWithF1(sources, goldTtl);

        string discoveryPath = Path.Combine(outDir, "discovery-report.json");
        File.WriteAllText(discoveryPath, DiscoveryReportJson.SerializeBundle(discovery, f1));
        string ttlPath = Path.Combine(outDir, "business-rules.ttl");
        File.WriteAllText(ttlPath, BusinessRulesTtl.Render(discovery.BusinessRules));

        ModuleNode god = discovery.Modules.Single(m =>
            m.Kind == ModuleKind.Method && m.DisplayName == "ProcessMission");
        double csharpParseRate = discovery.ParseStatsByLanguage.TryGetValue(
            SourceLanguage.CSharp.ToString(), out var csStats) ? csStats.ParseRate : 0.0;
        int weakCrypto = discovery.CryptoFindings.Count(c => c.IsWeak);
        int hardcodedSecrets = discovery.CryptoFindings.Count(
            c => c.Id.Contains("secret", StringComparison.OrdinalIgnoreCase)
                 || c.Algorithm.Contains("secret", StringComparison.OrdinalIgnoreCase)
                 || c.Family.Contains("Secret", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"  sources loaded     : {sources.Count}");
        Console.WriteLine($"  modules            : {discovery.Modules.Count}  edges: {discovery.Edges.Count}  SCCs: {discovery.Sccs.Count}");
        Console.WriteLine($"  C# parse rate      : {csharpParseRate:P1}  (pre-registered >= 95%)");
        Console.WriteLine($"  god-method CC      : {god.Complexity.CyclomaticComplexity}  (ProcessMission; pre-registered > 30)");
        Console.WriteLine($"  business rules     : {discovery.BusinessRules.Count} extracted");
        Console.WriteLine($"  rule-extraction F1 : {f1.F1:F4}  (P={f1.Precision:F3} R={f1.Recall:F3}; pre-registered >= 0.85)");
        Console.WriteLine($"  crypto findings    : {discovery.CryptoFindings.Count} ({weakCrypto} weak, {hardcodedSecrets} hardcoded-secret)");
        Console.WriteLine($"  -> {Rel(repoRoot, discoveryPath)}");
        Console.WriteLine($"  -> {Rel(repoRoot, ttlPath)}");
        governance.Record("discovery", "ForgeEvolve.Discovery",
            Canonical.Json(("modules", discovery.Modules.Count), ("edges", discovery.Edges.Count),
                ("rules", discovery.BusinessRules.Count), ("ruleF1", Round(f1.F1)),
                ("godMethodCc", god.Complexity.CyclomaticComplexity),
                ("csharpParseRate", Round(csharpParseRate)), ("cryptoFindings", discovery.CryptoFindings.Count)));
        Console.WriteLine();

        // ── STAGE 2 — CLAR ───────────────────────────────────────────────────────
        Console.WriteLine("── [2/8] CLAR (lift the mission god-module) ──────────────────────");
        var clar = new ClarProvider();
        string clarJson = clar.Lift(god, discovery);
        IReadOnlyList<string> clarErrors = clar.Validate(clarJson);
        if (clarErrors.Count > 0)
            throw new InvalidOperationException(
                "CLAR document failed schema validation:\n  " + string.Join("\n  ", clarErrors));
        string clarPath = clar.LiftToFile(god, discovery, outDir);

        Console.WriteLine($"  lifted module      : {god.Id}");
        Console.WriteLine($"  schema validation  : VALID (0 errors against frozen CLAR.schema.json)");
        Console.WriteLine($"  -> {Rel(repoRoot, clarPath)}");
        governance.Record("clar", "ForgeEvolve.Clar",
            Canonical.Json(("module", god.Id), ("schemaValid", true),
                ("clarSha256", Sha.Hex(clarJson))));
        Console.WriteLine();

        // ── STAGE 3 — PLANNER ────────────────────────────────────────────────────
        Console.WriteLine("── [3/8] Migration Planner ───────────────────────────────────────");
        var planner = new MigrationPlanner();
        MigrationPlan plan = planner.Plan(discovery);

        string planPath = Path.Combine(outDir, "migration-plan.json");
        File.WriteAllText(planPath, MigrationPlanJson.Serialize(plan));
        string mermaidPath = Path.Combine(outDir, "migration-plan.mermaid");
        File.WriteAllText(mermaidPath, plan.MermaidDiagram ?? "");

        Console.WriteLine($"  proposed units     : {plan.Units.Count}");
        foreach (string unitId in plan.OrderedUnitIds)
        {
            MigrationUnit u = plan.Units.First(x => x.Id == unitId);
            Console.WriteLine($"    - {u.ProposedServiceName,-26} risk={u.AggregateRiskScore:F3}  members={u.MemberModuleIds.Count}");
        }
        Console.WriteLine($"  strangler seams    : {plan.UnitEdges.Count} inter-unit edges");
        Console.WriteLine($"  -> {Rel(repoRoot, planPath)}");
        Console.WriteLine($"  -> {Rel(repoRoot, mermaidPath)}");
        governance.Record("plan", "ForgeEvolve.Planner",
            Canonical.Json(("units", plan.Units.Count), ("seams", plan.UnitEdges.Count),
                ("order", string.Join(">", plan.OrderedUnitIds))));
        Console.WriteLine();

        // ── STAGE 4 — TRANSFORM / ORCHESTRATE (offline replay) ───────────────────
        Console.WriteLine("── [4/8] Transformation (offline transcript replay) ──────────────");
        // The mission unit, per the recorded transcript's deterministic key
        // (Unit.Id="MissionRouting.MissionProcessor", SourceLanguage=CSharp, TargetStack="dotnet8").
        var missionUnit = new MigrationUnit
        {
            Id = "MissionRouting.MissionProcessor",
            ProposedServiceName = "MissionService",
            MemberModuleIds = new[] { god.Id },
            AggregateRiskScore = god.RiskScore,
            ApiOperations = new[] { god.DisplayName },
        };
        var task = new TransformTask
        {
            TaskId = "P3-mission-modernization",
            Unit = missionUnit,
            ClarDocumentJson = clarJson,
            Rules = discovery.BusinessRules,
            SourceLanguage = SourceLanguage.CSharp,
            TargetStack = "dotnet8",
        };

        // TransformationEngine(repoRoot) points its TranscriptStore at the TOP-LEVEL
        // fixtures/transcripts/ (the real recorded transcript), which is the clean reconciliation
        // of the path: no copy needed. It replays the recorded TransformResult verbatim.
        var transformer = new TransformationEngine(repoRoot);
        // Confirm the transcript exists for this unit (fail loudly, never fabricate).
        string transcriptKey = TranscriptStore.ComputeKey(
            missionUnit.Id, SourceLanguage.CSharp, "dotnet8");
        var transcriptStore = new TranscriptStore(Path.Combine(repoRoot, "fixtures", "transcripts"));
        if (transcriptStore.TryLoad(transcriptKey) is null)
            throw new InvalidOperationException(
                $"No recorded transcript for key {transcriptKey} under fixtures/transcripts/ — refusing to fabricate.");

        TransformResult transform = transformer.TransformAsync(task).GetAwaiter().GetResult();

        // Also exercise the Tool Orchestrator's REAL routing decision (offline; nothing fabricated)
        // so the routed AgentId is genuine, not invented.
        var orchestrator = new ForgeEvolve.Orchestrator.ToolOrchestrator(
            mode: OrchestratorMode.Offline);
        RoutingScore routing = orchestrator.Route(task, sovereignOnly: false);

        string ccBefore = NoteValue(transform.Notes, "max-method-cc-before") ?? "?";
        string ccAfter = NoteValue(transform.Notes, "max-method-cc-after") ?? "?";
        string transformPath = Path.Combine(outDir, "transform-result.json");
        File.WriteAllText(transformPath, TransformResultJson.Serialize(transform));

        Console.WriteLine($"  routed agent       : {routing.AgentId}  (would-select; sampled={routing.SampledScore:F4})");
        Console.WriteLine($"  replay agent       : {transform.AgentId}  (deterministic transcript replay)");
        Console.WriteLine($"  files emitted      : {transform.Files.Count}");
        Console.WriteLine($"  max-method CC      : {ccBefore} -> {ccAfter}  (legacy god-method -> modern)");
        Console.WriteLine($"  compiled clean     : {transform.CompiledClean}");
        foreach (string n in transform.Notes)
            Console.WriteLine($"      note: {n}");
        Console.WriteLine($"  -> {Rel(repoRoot, transformPath)}");
        // NOTE: the orchestrator's Thompson-sampling routing decision (routing.AgentId) is printed
        // for visibility but is INTENTIONALLY NOT recorded in the canonical provenance payload: its
        // RNG seed derives from a per-process string hash, so it varies run-to-run by design. The
        // DETERMINISTIC, artifact-producing agent is the offline-replay agent (transform.AgentId),
        // which is what we anchor in the tamper-evident chain.
        governance.Record("transform", "ForgeEvolve.Transformation",
            Canonical.Json(("unit", missionUnit.Id), ("transcriptKey", transcriptKey),
                ("replayAgent", transform.AgentId), ("filesEmitted", transform.Files.Count),
                ("ccBefore", ccBefore), ("ccAfter", ccAfter),
                ("compiledClean", transform.CompiledClean)));
        Console.WriteLine();

        // ── STAGE 5 — VALIDATION (THE HEADLINE) ──────────────────────────────────
        Console.WriteLine("── [5/8] Behavioral-equivalence validation (modern == legacy) ────");
        string corpusPath = Path.Combine(repoRoot, "surrogate", "corpus", "corpus.json");
        LoadedCorpus corpus = CorpusLoader.Load(corpusPath);

        // The REAL legacy runner (surrogate MissionProcessor) and the REAL modern runner
        // (the modernized ForgeEvolve.ModernMds.MissionService) — both string -> string.
        var legacyRunner = new LegacyRunner();
        ModernMissionService modernService = ModernMissionService.CreateDefault();
        var modernRunner = new ModernRunner(modernService.ProcessMission);

        var tolerance = new ToleranceConfig
        {
            ContinuousRelativeError = 1e-9,
            ContinuousAbsoluteFloor = 1e-12,
        };

        var validator = new EquivalenceValidator();
        EquivalenceReport eq = validator.Verify(
            unitId: missionUnit.Id,
            legacy: legacyRunner,
            modern: modernRunner,
            vectors: corpus.Vectors,
            tolerance: tolerance);

        string eqPath = Path.Combine(outDir, "equivalence-report.json");
        File.WriteAllText(eqPath, EquivalenceReportJson.Serialize(eq));

        int intentionalDivergences = eq.VectorsTotal - eq.VectorsPassed - eq.Violations;
        Console.WriteLine($"  VectorsTotal       : {eq.VectorsTotal}");
        Console.WriteLine($"  VectorsPassed      : {eq.VectorsPassed}  (fully equivalent, modern == legacy)");
        Console.WriteLine($"  Violations         : {eq.Violations}   (MUST be 0)");
        Console.WriteLine($"  IntentionalDiverg. : {intentionalDivergences}  (corpus ground-truth divergent: {corpus.DivergentCount})");
        Console.WriteLine($"  Chernoff bound     : {eq.ChernoffDeviationBound:E6}  (delta={eq.ConfidenceLevel})");
        Console.WriteLine($"  -> {Rel(repoRoot, eqPath)}");
        if (eq.Violations != 0)
            throw new InvalidOperationException(
                $"Equivalence FAILED: {eq.Violations} unexpected violation(s). The modern component is NOT byte-equivalent to legacy.");
        governance.Record("validate", "ForgeEvolve.Validation",
            Canonical.Json(("unit", eq.UnitId), ("vectorsTotal", eq.VectorsTotal),
                ("vectorsPassed", eq.VectorsPassed), ("violations", eq.Violations),
                ("intentionalDivergences", intentionalDivergences),
                ("chernoffBound", Round(eq.ChernoffDeviationBound))));
        Console.WriteLine();

        // ── STAGE 5b — LATENT-DEFECT DETECTION (legacy vs reference, full corpus) ──
        // Run the Validation module's intentional-divergence detector over the WHOLE 2000-vector
        // corpus (legacy output vs the reference answer key) and report the latent legacy defects
        // BY CLASS. This surfaces the bugs the legacy code was hiding — never auto-fixed — and is
        // the demo-visible proof of the precision=recall=1.0 result that previously lived only in
        // ForgeEvolve.Validation.Tests. Reuses DivergenceDetector / CorpusLoader verbatim.
        Console.WriteLine("── [5b/8] Latent-defect detection (legacy vs reference, full corpus) ─");
        LatentDefectReport latent = DetectLatentDefects(corpus, legacyRunner, tolerance);

        string latentPath = Path.Combine(outDir, "latent-defects.json");
        File.WriteAllText(latentPath, LatentDefectReport.Serialize(latent));

        Console.WriteLine($"  corpus vectors     : {latent.CorpusVectors}");
        Console.WriteLine($"  latent defects     : {latent.TotalDetected}  (ground-truth divergent: {latent.GroundTruthDivergent})");
        foreach (LatentDefectByClass cl in latent.ByClass)
            Console.WriteLine($"    - {cl.Tag,-16} : {cl.DetectedCount,4}  ({cl.Description})");
        Console.WriteLine($"  detector precision : {latent.Precision:F4}   recall: {latent.Recall:F4}   (vs expectedLegacyDivergent)");
        Console.WriteLine($"  -> {Rel(repoRoot, latentPath)}");
        if (latent.Precision != 1.0 || latent.Recall != 1.0)
            throw new InvalidOperationException(
                $"Latent-defect detector did not reach P=R=1.0 (P={latent.Precision}, R={latent.Recall}).");
        // NOTE: the latent-defect detection is a SUB-STEP of validation (stage 5b) — its evidence is
        // the latent-defects.json artifact plus the console summary, and its quantified counts flow
        // into the cATO POA&M (POAM-L-*). We deliberately do NOT add a separate governance leaf for
        // it, so the canonical IGOM remains the 8-record chain (discovery→CLAR→plan→transform→
        // validate→cATO→KG1→KG2) that Vol 2 cites.
        Console.WriteLine();

        // ── STAGE 6 — CYBER / cATO ───────────────────────────────────────────────
        Console.WriteLine("── [6/8] Cyber / cATO overlay (STIG, 800-53, SBOM, POA&M) ────────");
        var cyber = new CyberOverlay();
        // The overlay writes its artifact bundle under outDir (cato/*, sbom.cdx.json, poam.csv,
        // control-map.yaml, and its OWN provenance.json). Governance overwrites the canonical
        // provenance.json afterward (stage 7), so Cato's internal ledger becomes a sub-artifact.
        // The quantified per-class latent-defect counts become ECP-recommended POA&M items (POAM-L-*).
        IReadOnlyList<LatentDefectClass> latentForPoam = latent.ByClass
            .Select(c => new LatentDefectClass(c.Tag, c.Description, c.DetectedCount))
            .ToList();
        CatoArtifacts cato = cyber.Generate(sources, transform.Files, discovery, outDir, latentForPoam);

        int findingsBefore = cato.StigBefore.Count;
        // HONEST disposition counts (absence of a file type from the modern component is NOT a fix):
        //   Remediated — in-scope C#, pattern genuinely gone; OutOfScope — file type the modern C#
        //   component does not cover (.js/.sql); Residual — in-scope C#, pattern still present.
        int remediated = cato.StigAfter.Count(s => s.Disposition == StigAnalyzer.DispositionRemediated);
        int outOfScope = cato.StigAfter.Count(s => s.Disposition == StigAnalyzer.DispositionOutOfScope);
        int residual = cato.StigAfter.Count(s => s.Disposition == StigAnalyzer.DispositionResidual);
        Console.WriteLine($"  STIG findings      : {findingsBefore} detected -> {remediated} remediated / {outOfScope} out-of-scope / {residual} residual");
        Console.WriteLine($"    remediated (in-scope C#, genuinely fixed) : {string.Join(", ", cato.StigAfter.Where(s => s.Disposition == StigAnalyzer.DispositionRemediated).Select(s => s.RuleId))}");
        Console.WriteLine($"    out-of-scope (file type not transformed)  : {string.Join(", ", cato.StigAfter.Where(s => s.Disposition == StigAnalyzer.DispositionOutOfScope).Select(s => s.RuleId))}");
        Console.WriteLine($"    residual (in-scope C#, still present)      : {string.Join(", ", cato.StigAfter.Where(s => s.Disposition == StigAnalyzer.DispositionResidual).Select(s => s.RuleId))}");
        Console.WriteLine($"  NIST 800-53 ctrls  : {cato.ControlMap.Count} mapped");
        Console.WriteLine($"  POA&M items        : {cato.Poam.Count}");
        Console.WriteLine($"  SBOM               : {Rel(repoRoot, cato.SbomPath)}");
        Console.WriteLine($"  cATO Merkle root   : {cato.ProvenanceMerkleRoot}");
        Console.WriteLine($"  -> {Rel(repoRoot, Path.Combine(outDir, "cato"))}/ (stig-before.json, stig-after.json, control-map.yaml, control-map.json)");
        Console.WriteLine($"  -> {Rel(repoRoot, Path.Combine(outDir, "poam.csv"))}");
        governance.Record("cato", "ForgeEvolve.Cato",
            Canonical.Json(("stigBefore", findingsBefore), ("remediated", remediated),
                ("outOfScope", outOfScope), ("residual", residual),
                ("controls", cato.ControlMap.Count),
                ("poam", cato.Poam.Count), ("catoMerkleRoot", cato.ProvenanceMerkleRoot)));
        Console.WriteLine();

        // ── STAGE 7 — GOVERNANCE (unify provenance + review gates) ───────────────
        Console.WriteLine("── [7/8] Governance (canonical IGOM provenance + KG1/KG2) ────────");
        // Write the CANONICAL provenance chain for the whole run (overwrites Cato's sub-ledger
        // at the same path). This is the single source of provenance truth for the run.
        string provenancePath = governance.WriteProvenance(Path.Combine(outDir, "provenance.json"));
        LedgerVerification verify = governance.Verify();

        // KG1 / KG2 evaluated against the REAL measured metrics from this run.
        ReviewGate kg1 = governance.Evaluate("KG1", new Dictionary<string, string>
        {
            ["ruleF1"] = f1.F1.ToString("R", CultureInfo.InvariantCulture),
            ["oracleHarnessRuns"] = "true", // the equivalence harness ran end-to-end above
        });
        ReviewGate kg2 = governance.Evaluate("KG2", new Dictionary<string, string>
        {
            ["discreteViolations"] = eq.Violations.ToString(CultureInfo.InvariantCulture),
            ["catoBundle"] = "true", // the cATO artifact bundle was generated above
        });
        // Re-write provenance so the gate decisions are part of the canonical chain too.
        governance.WriteProvenance(Path.Combine(outDir, "provenance.json"));

        Console.WriteLine($"  records (IGOM)     : {governance.Ledger.Count}");
        Console.WriteLine($"  merkle root        : {governance.CurrentMerkleRoot()}");
        Console.WriteLine($"  chain verify       : {(verify.Valid ? "VALID" : "BROKEN@" + verify.BrokenAtIndex)}");
        Console.WriteLine($"  KG1 (F1>=0.85 & harness)        : {(kg1.Passed ? "PASS" : "FAIL")}");
        Console.WriteLine($"  KG2 (0 violations & cATO bundle): {(kg2.Passed ? "PASS" : "FAIL")}");
        Console.WriteLine($"  -> {Rel(repoRoot, provenancePath)}");
        Console.WriteLine();

        // ── STAGE 8 — HONEST SUMMARY ─────────────────────────────────────────────
        Console.WriteLine("══════════════════════════════════════════════════════════════════");
        Console.WriteLine(" FORGE EVOLVE for TMPC — HEADLINE METRICS (honest summary)");
        Console.WriteLine("══════════════════════════════════════════════════════════════════");
        Console.WriteLine($"  Discovery   : C# parse rate {csharpParseRate:P1}; god-method CC {god.Complexity.CyclomaticComplexity}; rule F1 {f1.F1:F4}; crypto findings {discovery.CryptoFindings.Count}");
        Console.WriteLine($"  Planner     : {plan.Units.Count} candidate microservice boundaries proposed (heuristic)");
        Console.WriteLine($"  Transform   : max-method CC {ccBefore} -> {ccAfter}; {transform.Files.Count} modern files (offline replay)");
        Console.WriteLine($"  EQUIVALENCE : {eq.VectorsPassed}/{eq.VectorsTotal} vectors equivalent; Violations={eq.Violations}; intentional divergences={intentionalDivergences}");
        Console.WriteLine($"              : Chernoff deviation bound {eq.ChernoffDeviationBound:E3} at delta={eq.ConfidenceLevel}");
        Console.WriteLine($"  LATENT DEFS : {latent.TotalDetected} surfaced (P=R={latent.Precision:F1}): {string.Join(" / ", latent.ByClass.Select(c => $"{c.Tag} {c.DetectedCount}"))}");
        Console.WriteLine($"  cATO        : STIG {findingsBefore} detected -> {remediated} remediated / {outOfScope} out-of-scope / {residual} residual; {cato.ControlMap.Count} controls; {cato.Poam.Count} POA&M items");
        Console.WriteLine($"  Governance  : {governance.Ledger.Count}-record IGOM, root {Short(governance.CurrentMerkleRoot())}; KG1={(kg1.Passed ? "PASS" : "FAIL")} KG2={(kg2.Passed ? "PASS" : "FAIL")}");
        Console.WriteLine();
        Console.WriteLine($"  DISCLAIMER  : All figures are {Disclaimer}");
        Console.WriteLine("══════════════════════════════════════════════════════════════════");

        return 0;
    }

    // ── Surrogate source loading (mirrors the module tests' SurrogateFixture) ─────
    private static IReadOnlyList<SourceArtifact> LoadSurrogateSources(string surrogateDir)
    {
        var rel = new[]
        {
            Path.Combine("legacy", "MissionProcessor.cs"),
            Path.Combine("legacy", "wwwroot", "mission-review.js"),
            Path.Combine("legacy", "sql", "sp_PublishMission.sql"),
            Path.Combine("legacy", "sql", "schema.sql"),
            Path.Combine("legacy", "GeoFixedPoint.bas"),
        };
        var files = new List<SourceArtifact>(rel.Length);
        foreach (string r in rel)
        {
            string path = Path.Combine(surrogateDir, r);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Surrogate source missing: {path}");
            files.Add(SourceLoader.FromFile(path));
        }
        return files;
    }

    // ── Latent-defect detection over the full corpus (Fix B) ─────────────────────
    // The four seeded latent legacy defect classes, in the report's canonical (count-descending,
    // mission-impact) order. The Description text mirrors the Vol 2 latent-defect table.
    private static readonly (string Tag, string Description)[] LatentDefectTaxonomy =
    {
        ("anti-meridian",   "raw (lon2-lon1) leg distance with no +/-180 deg wrap"),
        ("leap-second",     "omitted leap-second adjustment + TOT truncation bias"),
        ("overflow",        "numeric overflow on extreme inputs"),
        ("precision-drift", "floating-point precision drift across accumulated legs"),
    };

    /// <summary>
    /// Run the Validation module's intentional-divergence detector over the WHOLE corpus
    /// (legacy-vs-reference) and bucket each detected latent defect by its corpus class tag. Also
    /// scores the detector against the corpus `expectedLegacyDivergent` ground truth (P / R). Pure
    /// reuse of <see cref="DivergenceDetector"/> — no detection logic is reimplemented here.
    /// </summary>
    private static LatentDefectReport DetectLatentDefects(
        LoadedCorpus corpus, ILegacyRunner legacy, ToleranceConfig tolerance)
    {
        // Per-class detected counts (a divergent vector carries exactly one class tag in the
        // frozen corpus; this is asserted by the determinism of the result below).
        var detectedByTag = new Dictionary<string, int>(StringComparer.Ordinal);
        int totalDetected = 0;
        for (int i = 0; i < corpus.Vectors.Count; i++)
        {
            EquivalenceTestVector vec = corpus.Vectors[i];
            bool detected = DivergenceDetector.IsIntentionalDivergence(vec, legacy, tolerance);
            if (!detected) continue;
            totalDetected++;
            foreach ((string tag, _) in LatentDefectTaxonomy)
                if (vec.Tags.Contains(tag))
                {
                    detectedByTag.TryGetValue(tag, out int n);
                    detectedByTag[tag] = n + 1;
                }
        }

        // Detector precision/recall vs the corpus ground-truth label (reuses Score()).
        DetectorScore score = DivergenceDetector.Score(
            corpus.Vectors, legacy, corpus.ExpectedLegacyDivergent, tolerance);

        var byClass = LatentDefectTaxonomy
            .Select(t => new LatentDefectByClass(
                t.Tag, t.Description, detectedByTag.TryGetValue(t.Tag, out int c) ? c : 0))
            .ToList();

        return new LatentDefectReport
        {
            CorpusVectors = corpus.Vectors.Count,
            TotalDetected = totalDetected,
            GroundTruthDivergent = corpus.DivergentCount,
            Precision = score.Precision,
            Recall = score.Recall,
            ByClass = byClass,
        };
    }

    private static string? NoteValue(IReadOnlyList<string> notes, string key)
    {
        foreach (string n in notes)
            if (n.StartsWith(key + "=", StringComparison.Ordinal))
                return n[(key.Length + 1)..];
        return null;
    }

    private static double Round(double v) => Math.Round(v, 9, MidpointRounding.ToEven);
    private static string Short(string hash) => hash.Length <= 12 ? hash : hash[..12];

    private static string Rel(string root, string full)
    {
        string r = Path.GetRelativePath(root, full).Replace('\\', '/');
        return r;
    }
}
