// ─────────────────────────────────────────────────────────────────────────────
// CyberOverlay — Stage 5 of the FORGE EVOLVE for TMPC pipeline.
//
// Implements ICyberOverlay.Generate(legacy, modern, discovery, outputDir) and emits the
// continuous-ATO (cATO) artifact bundle:
//
//   cato/stig-before.json     — STIG findings in the legacy code (real, present)
//   cato/stig-after.json      — reconciled findings; RemediatedByTransform where the
//                               modern code fixes the class
//   cato/control-map.yaml     — NIST SP 800-53 Rev 5 control map (+ machine JSON)
//   cato/control-map.json
//   sbom.cdx.json             — CycloneDX 1.5 SBOM for the analyzed component
//   provenance.json           — SHA-256 hashchain + Merkle root (AU-10 non-repudiation)
//   poam.csv                  — Plan of Action & Milestones (+ machine JSON)
//   poam.json
//
// Returns CatoArtifacts (StigBefore/After, ControlMap, Poam, SbomPath, ProvenanceMerkleRoot).
//
// Determinism: artifacts are produced from the inputs with stable ordering and fixed
// timestamps so repeated runs over the same inputs yield byte-identical files (the Merkle
// root is therefore stable and citable).
//
// Path safety (this module is itself security-relevant): outputDir is canonicalized once to
// an absolute base, and every write goes through WriteUnder(), which re-resolves the target
// and refuses to write outside the base — defense against path traversal (CWE-22). Relative
// artifact names are fixed string constants below, never caller-controlled.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Cato;

public sealed class CyberOverlay : ICyberOverlay
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public CatoArtifacts Generate(
        IReadOnlyList<SourceArtifact> legacy,
        IReadOnlyList<EmittedFile> modern,
        DiscoveryReport discovery,
        string outputDir)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(modern);
        ArgumentNullException.ThrowIfNull(discovery);
        if (string.IsNullOrWhiteSpace(outputDir))
            throw new ArgumentException("outputDir is required", nameof(outputDir));

        // Canonicalize the output directory to a single absolute base. All writes are confined
        // here (see WriteUnder). Fixed relative names guarantee no traversal regardless of input.
        string baseDir = Path.GetFullPath(outputDir);
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(Path.Combine(baseDir, "cato"));

        // ── 1. STIG before/after ────────────────────────────────────────────────
        IReadOnlyList<StigFinding> stigBefore = StigAnalyzer.ScanLegacy(legacy);
        IReadOnlyList<StigFinding> stigAfter = StigAnalyzer.ScanModernAndReconcile(stigBefore, modern);

        // ── 2. NIST 800-53 control map ──────────────────────────────────────────
        const string sbomRel = "sbom.cdx.json";
        const string provenanceRel = "provenance.json";
        IReadOnlyList<ControlMapping> controlMap =
            ControlMapBuilder.Build(stigBefore, sbomRel, provenanceRel);

        // ── 3. CycloneDX SBOM ───────────────────────────────────────────────────
        // Package refs of the analyzed component. The surrogate legacy project references
        // Microsoft.Data.SqlClient; the overlay itself references the frozen Contracts.
        var packages = new List<SbomComponent>
        {
            new("Microsoft.Data.SqlClient", "5.2.0"),
            new("ForgeEvolve.Contracts", "1.0.0"),
        };
        // Use a deterministic seed derived from the legacy inputs so the serialNumber is stable.
        string sbomSeed = ProvenanceLedger.Sha256Hex(
            string.Join("|", legacy.Select(l => l.ContentSha256).OrderBy(x => x, StringComparer.Ordinal)));
        string sbomJson = SbomBuilder.Build(
            componentName: "tmpc-surrogate-mds",
            componentVersion: "1.0.0",
            packages: packages,
            serialSeed: sbomSeed);
        string sbomPath = WriteUnder(baseDir, sbomRel, sbomJson);

        // ── 4. Hashchain provenance (Merkle root) ───────────────────────────────
        var ledger = new ProvenanceLedger();
        // Anchor each input artifact's content hash, then each emitted cATO step. The
        // payloads are the artifact digests / serialized findings — deterministic.
        foreach (SourceArtifact src in legacy.OrderBy(l => l.Path, StringComparer.Ordinal))
        {
            ledger.Append("ingest-legacy", "ForgeEvolve.Cato",
                $"{{\"path\":\"{src.Path}\",\"sha256\":\"{src.ContentSha256}\"}}");
        }
        ledger.Append("stig-scan-before", "ForgeEvolve.Cato.StigAnalyzer",
            JsonSerializer.Serialize(stigBefore, JsonOpts));
        ledger.Append("stig-scan-after", "ForgeEvolve.Cato.StigAnalyzer",
            JsonSerializer.Serialize(stigAfter, JsonOpts));
        ledger.Append("control-map", "ForgeEvolve.Cato.ControlMapBuilder",
            JsonSerializer.Serialize(controlMap, JsonOpts));
        ledger.Append("sbom", "ForgeEvolve.Cato.SbomBuilder",
            ProvenanceLedger.Sha256Hex(sbomJson));

        // ── 5. POA&M ────────────────────────────────────────────────────────────
        IReadOnlyList<PoamItem> poam = PoamBuilder.Build(stigAfter, discovery);
        ledger.Append("poam", "ForgeEvolve.Cato.PoamBuilder",
            JsonSerializer.Serialize(poam, JsonOpts));

        string merkleRoot = ledger.MerkleRoot();

        // ── Write all artifacts (fixed relative names, confined to baseDir) ──────
        WriteUnder(baseDir, Path.Combine("cato", "stig-before.json"),
            JsonSerializer.Serialize(stigBefore, JsonOpts));
        WriteUnder(baseDir, Path.Combine("cato", "stig-after.json"),
            JsonSerializer.Serialize(stigAfter, JsonOpts));
        WriteUnder(baseDir, Path.Combine("cato", "control-map.yaml"),
            ControlMapBuilder.ToYaml(controlMap));
        WriteUnder(baseDir, Path.Combine("cato", "control-map.json"),
            JsonSerializer.Serialize(controlMap, JsonOpts));
        WriteUnder(baseDir, "poam.csv", PoamBuilder.ToCsv(poam));
        WriteUnder(baseDir, "poam.json", JsonSerializer.Serialize(poam, JsonOpts));

        var provenanceDoc = new
        {
            merkleRoot,
            algorithm = "SHA-256",
            linkFunction = "EntryHash = SHA256(PayloadSha256 + \"|\" + PrevHash)",
            merkleTree = "binary, duplicate-last padding (Bitcoin-style)",
            genesisPrevHash = ProvenanceLedger.GenesisHash,
            entryCount = ledger.Entries.Count,
            entries = ledger.Entries,
        };
        WriteUnder(baseDir, provenanceRel, JsonSerializer.Serialize(provenanceDoc, JsonOpts));

        // ── Assemble & return ───────────────────────────────────────────────────
        return new CatoArtifacts
        {
            StigBefore = stigBefore,
            StigAfter = stigAfter,
            ControlMap = controlMap,
            Poam = poam,
            SbomPath = sbomPath,
            ProvenanceMerkleRoot = merkleRoot,
        };
    }

    /// <summary>
    /// Write <paramref name="content"/> to <paramref name="relativeName"/> resolved under the
    /// canonical <paramref name="baseDir"/>. Refuses to escape the base (CWE-22 path traversal
    /// guard). Returns the absolute path written.
    /// </summary>
    private static string WriteUnder(string baseDir, string relativeName, string content)
    {
        string full = Path.GetFullPath(Path.Combine(baseDir, relativeName));
        string baseWithSep = baseDir.EndsWith(Path.DirectorySeparatorChar)
            ? baseDir
            : baseDir + Path.DirectorySeparatorChar;
        if (!full.StartsWith(baseWithSep, StringComparison.Ordinal))
            throw new InvalidOperationException($"Refusing to write outside output directory: {relativeName}");

        string? dir = Path.GetDirectoryName(full);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(full, content);
        return full;
    }
}
