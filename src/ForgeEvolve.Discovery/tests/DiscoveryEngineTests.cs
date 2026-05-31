// ─────────────────────────────────────────────────────────────────────────────
// DiscoveryEngineTests — acceptance tests over the ACTUAL surrogate files.
//
// PART OF: FORGE EVOLVE for TMPC, Discovery Engine tests (workstream WS-A).
//
// Asserts the pre-registered metrics: god-method CC > 30, C# parse rate >= 0.95, the categorical
// tasking rule + leg/turn/TOT rules are extracted, rule-extraction F1 >= 0.85, and at least one
// SCC / coupling cluster is found. Also exercises the JSON serialization helper and the crypto
// inventory (honest: zero algorithm-crypto, one hardcoded-secret finding).
// ─────────────────────────────────────────────────────────────────────────────

using ForgeEvolve.Contracts;
using Xunit;

namespace ForgeEvolve.Discovery.Tests;

public sealed class DiscoveryEngineTests : IClassFixture<SurrogateFixture>
{
    private readonly SurrogateFixture _fx;
    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public DiscoveryEngineTests(SurrogateFixture fx, Xunit.Abstractions.ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    [Fact]
    public void PrintHeadlineMetrics()
    {
        var god = _fx.Report.Modules.Single(m =>
            m.Kind == ModuleKind.Method && m.DisplayName == "ProcessMission");
        var cs = _fx.Report.ParseStatsByLanguage["CSharp"];
        var clusters = _fx.Report.Sccs.Where(s => s.MemberIds.Count > 1).ToList();
        int algoCrypto = _fx.Report.CryptoFindings.Count(f => f.Family != "Secret");

        _out.WriteLine("===== DISCOVERY ENGINE — HEADLINE METRICS (surrogate) =====");
        _out.WriteLine($"God-method CC (ProcessMission) : {god.Complexity.CyclomaticComplexity}");
        _out.WriteLine($"C# parse rate                  : {cs.ParseRate:P1} ({cs.FilesParsed}/{cs.FilesTotal})");
        _out.WriteLine($"Extracted business-rule count  : {_fx.Report.BusinessRules.Count}");
        _out.WriteLine($"Rule-extraction F1             : {_fx.F1.F1:F4} " +
                       $"(P={_fx.F1.Precision:F3} R={_fx.F1.Recall:F3} TP={_fx.F1.TruePositives} " +
                       $"FP={_fx.F1.FalsePositives} FN={_fx.F1.FalseNegatives}, gold={_fx.F1.GoldCount})");
        _out.WriteLine($"Crypto findings (total)        : {_fx.Report.CryptoFindings.Count} " +
                       $"(weak-algorithm crypto={algoCrypto}, hardcoded-secret={_fx.Report.CryptoFindings.Count - algoCrypto})");
        _out.WriteLine($"SCC coupling clusters (size>1) : {clusters.Count}");
        _out.WriteLine($"Modules / Edges / SCCs         : {_fx.Report.Modules.Count} / {_fx.Report.Edges.Count} / {_fx.Report.Sccs.Count}");
        Assert.True(true);
    }

    [Fact]
    public void GodMethod_ProcessMission_HasCyclomaticComplexityAbove30()
    {
        var god = _fx.Report.Modules.Single(m =>
            m.Kind == ModuleKind.Method && m.DisplayName == "ProcessMission");
        Assert.True(god.Complexity.CyclomaticComplexity > 30,
            $"Expected ProcessMission CC > 30 but was {god.Complexity.CyclomaticComplexity}.");
    }

    [Fact]
    public void CSharp_ParseRate_IsAtLeast95Percent()
    {
        var cs = _fx.Report.ParseStatsByLanguage["CSharp"];
        Assert.True(cs.ParseRate >= 0.95,
            $"Expected C# parse rate >= 0.95 but was {cs.ParseRate:P1} ({cs.FilesParsed}/{cs.FilesTotal}).");
    }

    [Fact]
    public void CategoricalTaskingRule_IsExtracted()
    {
        var rule = _fx.Report.BusinessRules.SingleOrDefault(r =>
            r.Category == BusinessRuleCategory.Constraint &&
            r.Statement.Contains("MST", StringComparison.Ordinal) &&
            r.Statement.Contains("SSN", StringComparison.Ordinal));
        Assert.NotNull(rule);
        Assert.Contains("taskingGoNoGo", rule!.Expression);
    }

    [Theory]
    [InlineData(BusinessRuleCategory.Validation, "degree-box")]   // leg feasibility
    [InlineData(BusinessRuleCategory.Validation, "turn")]          // turn-rate limit
    [InlineData(BusinessRuleCategory.Validation, "time-on-target")]// TOT tolerance
    public void KeyValidationRules_AreExtracted(BusinessRuleCategory category, string keyword)
    {
        bool found = _fx.Report.BusinessRules.Any(r =>
            r.Category == category &&
            r.Statement.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        Assert.True(found, $"Expected a {category} rule mentioning '{keyword}'.");
    }

    [Fact]
    public void LegLengthRule_IsExtracted()
    {
        bool found = _fx.Report.BusinessRules.Any(r =>
            r.Category == BusinessRuleCategory.Constraint &&
            (r.Statement.Contains("leg", StringComparison.OrdinalIgnoreCase) &&
             r.Statement.Contains("1500", StringComparison.Ordinal)));
        Assert.True(found, "Expected the maximum leg-length (1500 nm) constraint rule.");
    }

    [Fact]
    public void RuleExtraction_F1_IsAtLeast085()
    {
        Assert.True(_fx.F1.F1 >= 0.85,
            $"Expected rule-extraction F1 >= 0.85 but was {_fx.F1.F1:F4} " +
            $"(P={_fx.F1.Precision:F3} R={_fx.F1.Recall:F3} TP={_fx.F1.TruePositives} " +
            $"FP={_fx.F1.FalsePositives} FN={_fx.F1.FalseNegatives}).");
    }

    [Fact]
    public void F1_Matching_IsDiscriminating_NotALabelLookup()
    {
        // A deliberately UNRELATED extracted rule (right category, wrong concept) must NOT be
        // credited as a true positive — proving the Jaccard semantic match is real, not a lookup.
        var gold = RuleF1Scorer.ParseGoldTtl(_fx.GoldTtl);
        var bogus = new[]
        {
            new BusinessRule
            {
                Id = "bogus-1",
                Category = BusinessRuleCategory.Validation,
                Statement = "The user interface background colour shall be navy blue on Tuesdays.",
                Expression = "ui.background == navy",
                SourceRefs = new[] { "n/a" },
                Confidence = 0.5,
            },
        };
        var report = RuleF1Scorer.Score(bogus, gold);
        Assert.Equal(0, report.TruePositives);
    }

    [Fact]
    public void DependencyGraph_HasAtLeastOneCouplingCluster()
    {
        var clusters = _fx.Report.Sccs.Where(s => s.MemberIds.Count > 1).ToList();
        Assert.NotEmpty(clusters);
        // The god class + its members should be one tight cluster.
        Assert.Contains(clusters, c =>
            c.MemberIds.Any(id => id.EndsWith("ProcessMission", StringComparison.Ordinal)) &&
            c.MemberIds.Count >= 2);
    }

    [Fact]
    public void DependencyGraph_HasCallAndDataAccessEdges()
    {
        Assert.Contains(_fx.Report.Edges, e => e.Kind == DependencyKind.Calls);
        Assert.Contains(_fx.Report.Edges, e => e.Kind == DependencyKind.DataAccess);
    }

    [Fact]
    public void CrossLanguage_AllFourLanguagesParsed()
    {
        foreach (var lang in new[] { "CSharp", "JavaScript", "Sql", "Vb6" })
        {
            Assert.True(_fx.Report.ParseStatsByLanguage.ContainsKey(lang),
                $"Missing parse stats for {lang}.");
            var s = _fx.Report.ParseStatsByLanguage[lang];
            Assert.True(s.FilesParsed > 0, $"Expected at least one parsed {lang} file.");
            Assert.Equal(1.0, s.ParseRate, 3);
        }
    }

    [Fact]
    public void CryptoInventory_ReportsHardcodedSecret_AndZeroWeakAlgorithms()
    {
        // The surrogate has NO cryptographic primitives (so algorithm-crypto count must be 0)
        // but DOES contain a hardcoded connection string in LegacyConfig.
        int algoFindings = _fx.Report.CryptoFindings.Count(f => f.Family != "Secret");
        Assert.Equal(0, algoFindings);

        var secret = _fx.Report.CryptoFindings.SingleOrDefault(f => f.Family == "Secret");
        Assert.NotNull(secret);
        Assert.True(secret!.IsWeak);
        Assert.Contains("MissionProcessor.cs", secret.Location, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_SerializesToJson_WithExpectedFields()
    {
        string json = DiscoveryReportJson.Serialize(_fx.Report);
        Assert.Contains("\"Modules\"", json);
        Assert.Contains("\"Sccs\"", json);
        Assert.Contains("\"BusinessRules\"", json);
        Assert.Contains("\"CryptoFindings\"", json);
        Assert.Contains("\"ParseStatsByLanguage\"", json);
        // Enum serialized as string (JsonStringEnumConverter on the contract enums).
        Assert.Contains("\"Method\"", json);

        string bundle = DiscoveryReportJson.SerializeBundle(_fx.Report, _fx.F1);
        Assert.Contains("ruleExtractionF1", bundle);
    }

    [Fact]
    public void Tarjan_OnAKnownCyclicGraph_FindsTheCycle()
    {
        // Direct unit test of Tarjan independent of the surrogate: A->B->C->A is one SCC.
        var nodes = new[] { "A", "B", "C", "D" };
        var adj = new Dictionary<string, List<string>>
        {
            ["A"] = new() { "B" },
            ["B"] = new() { "C" },
            ["C"] = new() { "A" },
            ["D"] = new() { "C" }, // D is its own singleton SCC
        };
        var sccs = DependencyGraph.Tarjan(nodes, adj);
        Assert.Contains(sccs, s => s.MemberIds.Count == 3 &&
            s.MemberIds.Contains("A") && s.MemberIds.Contains("B") && s.MemberIds.Contains("C"));
        Assert.Contains(sccs, s => s.MemberIds.Count == 1 && s.MemberIds.Contains("D"));
    }
}
