// ─────────────────────────────────────────────────────────────────────────────
// DiscoveryEngine — IDiscoveryEngine implementation (Stage 1, workstream WS-A).
//
// PART OF: FORGE EVOLVE for TMPC, Discovery Engine.
//
// Pipeline:
//   1. Partition sources by language.
//   2. C# (Roslyn): complexity vectors + ModuleNodes (Type/Method) — CSharpAnalyzer.
//   3. Dependency graph (Calls/References/DataAccess) + Tarjan SCC — DependencyGraph.
//   4. Cross-language parse stats (JS/SQL/VB6 heuristic) + their ModuleNodes — CrossLanguageParser.
//   5. Offline deterministic business-rule extraction — RuleExtractor.
//   6. Weak-crypto / hardcoded-secret inventory — CryptoInventory.
//   7. Assemble DiscoveryReport (frozen contract). The rule-extraction F1 vs. the gold TTL is a
//      separate, optional computation exposed via AnalyzeWithF1 / RuleF1Scorer (the contract's
//      DiscoveryReport intentionally does not carry the F1; it is a governance metric).
// ─────────────────────────────────────────────────────────────────────────────

using ForgeEvolve.Contracts;

namespace ForgeEvolve.Discovery;

public sealed class DiscoveryEngine : IDiscoveryEngine
{
    /// <inheritdoc />
    public DiscoveryReport Analyze(IReadOnlyList<SourceArtifact> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        // 1) Partition by language.
        var csharp = sources.Where(s => s.Language == SourceLanguage.CSharp).ToList();
        var js = sources.Where(s => s.Language == SourceLanguage.JavaScript).ToList();
        var sql = sources.Where(s => s.Language == SourceLanguage.Sql).ToList();
        var vb6 = sources.Where(s => s.Language == SourceLanguage.Vb6).ToList();

        // 2) C# analysis (Roslyn).
        var cs = CSharpAnalyzer.Analyze(csharp);

        // 3) Dependency graph + Tarjan SCC.
        var graph = DependencyGraph.Build(cs);

        // 4) Cross-language parse stats + module nodes.
        var jsR = CrossLanguageParser.ParseJavaScript(js);
        var sqlR = CrossLanguageParser.ParseSql(sql);
        var vbR = CrossLanguageParser.ParseVb6(vb6);

        // 5) Offline rule extraction.
        var rules = RuleExtractor.Extract(cs, sources);

        // 6) Crypto / secret inventory.
        var crypto = CryptoInventory.Scan(sources);

        // ── Assemble ModuleNodes (C# types + methods, with FanIn filled in) ───────
        var modules = new List<ModuleNode>();
        foreach (var t in cs.Types)
        {
            modules.Add(new ModuleNode
            {
                Id = t.Id,
                DisplayName = t.DisplayName,
                Kind = ModuleKind.Type,
                Language = SourceLanguage.CSharp,
                SourcePath = t.SourcePath,
                StartLine = t.StartLine,
                EndLine = t.EndLine,
                Complexity = WithFanIn(t.Complexity, graph.FanInById, t.Id),
            });
        }
        foreach (var m in cs.Methods)
        {
            modules.Add(new ModuleNode
            {
                Id = m.Id,
                DisplayName = m.DisplayName,
                Kind = ModuleKind.Method,
                Language = SourceLanguage.CSharp,
                SourcePath = m.SourcePath,
                StartLine = m.StartLine,
                EndLine = m.EndLine,
                Complexity = WithFanIn(m.Complexity, graph.FanInById, m.Id),
            });
        }
        // Cross-language module nodes.
        modules.AddRange(jsR.Modules);
        modules.AddRange(sqlR.Modules);
        modules.AddRange(vbR.Modules);

        // ── Parse stats per language (keyed by SourceLanguage name) ───────────────
        var parseStats = new Dictionary<string, ParseStats>
        {
            [SourceLanguage.CSharp.ToString()]     = new() { FilesParsed = cs.FilesParsed,  FilesTotal = cs.FilesTotal },
            [SourceLanguage.JavaScript.ToString()] = new() { FilesParsed = jsR.FilesParsed, FilesTotal = jsR.FilesTotal },
            [SourceLanguage.Sql.ToString()]        = new() { FilesParsed = sqlR.FilesParsed, FilesTotal = sqlR.FilesTotal },
            [SourceLanguage.Vb6.ToString()]        = new() { FilesParsed = vbR.FilesParsed, FilesTotal = vbR.FilesTotal },
        };

        return new DiscoveryReport
        {
            Modules = modules,
            Edges = graph.Edges,
            Sccs = graph.Sccs,
            BusinessRules = rules,
            CryptoFindings = crypto,
            ParseStatsByLanguage = parseStats,
        };
    }

    /// <summary>
    /// Convenience overload: run Analyze AND score the extracted rules' F1 against the gold TTL.
    /// The DiscoveryReport (frozen contract) is returned as-is; the F1 report is returned alongside
    /// so callers/tests/governance can assert the pre-registered ≥0.85 threshold.
    /// </summary>
    public (DiscoveryReport report, RuleF1Report f1) AnalyzeWithF1(
        IReadOnlyList<SourceArtifact> sources,
        string goldTtl)
    {
        var report = Analyze(sources);
        var gold = RuleF1Scorer.ParseGoldTtl(goldTtl);
        var f1 = RuleF1Scorer.Score(report.BusinessRules, gold);
        return (report, f1);
    }

    private static ComplexityVector WithFanIn(ComplexityVector v, IReadOnlyDictionary<string, int> fanIn, string id)
    {
        int fi = fanIn.TryGetValue(id, out var f) ? f : 0;
        return v with { FanIn = fi };
    }
}
