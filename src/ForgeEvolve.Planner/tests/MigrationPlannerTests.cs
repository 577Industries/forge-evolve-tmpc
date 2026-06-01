// ─────────────────────────────────────────────────────────────────────────────
// MigrationPlannerTests — acceptance tests over the REAL Discovery -> Planner path on the surrogate.
//
// PART OF: FORGE EVOLVE for TMPC, Migration Planner tests (workstream WS-C).
//
// Asserts the WS-C acceptance criteria:
//   * the god-class coupling cluster is split into >= 3 candidate migration units;
//   * every module gets a composite RiskScore in [0,1];
//   * the highest-risk unit contains the god method (ProcessMission);
//   * OrderedUnitIds is a VALID topological order of the unit DAG (no edge points backward);
//   * the Mermaid diagram is non-empty and references the unit ids.
// Plus: the four candidate microservice boundaries resolve, the JSON helper round-trips, and the
// per-module risk scoring is exercised directly.
// ─────────────────────────────────────────────────────────────────────────────

using ForgeEvolve.Contracts;
using Xunit;

namespace ForgeEvolve.Planner.Tests;

public sealed class MigrationPlannerTests : IClassFixture<SurrogateFixture>
{
    private readonly SurrogateFixture _fx;
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public MigrationPlannerTests(SurrogateFixture fx, Xunit.Abstractions.ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    [Fact]
    public void PrintProposedPlan()
    {
        var plan = _fx.Plan;
        _out.WriteLine("===== MIGRATION PLANNER — PROPOSED PLAN (surrogate, heuristic) =====");
        _out.WriteLine($"Units: {plan.Units.Count}   Unit edges (seams): {plan.UnitEdges.Count}");
        _out.WriteLine("");
        _out.WriteLine("Proposed units (name | aggregate risk):");
        foreach (var u in plan.Units.OrderByDescending(u => u.AggregateRiskScore))
            _out.WriteLine($"  {u.ProposedServiceName,-28} {u.AggregateRiskScore:F3}  [{u.Id}]  members={u.MemberModuleIds.Count}");
        _out.WriteLine("");
        _out.WriteLine("Recommended migration order (lowest-risk / least-dependent first):");
        for (int i = 0; i < plan.OrderedUnitIds.Count; i++)
        {
            var u = plan.Units.First(x => x.Id == plan.OrderedUnitIds[i]);
            _out.WriteLine($"  {i + 1,2}. {u.ProposedServiceName,-28} risk={u.AggregateRiskScore:F3}  [{u.Id}]");
        }
        Assert.True(true);
    }

    // ── Decomposition: the god cluster splits into >= 3 candidate units ──────────

    [Fact]
    public void GodClassCluster_IsSplitInto_AtLeastThreeCandidateUnits()
    {
        // The Discovery engine returns the god class + its members as one large SCC.
        var godScc = _fx.Report.Sccs
            .Where(s => s.MemberIds.Any(id => id.EndsWith("ProcessMission", StringComparison.Ordinal)))
            .OrderByDescending(s => s.MemberIds.Count)
            .First();
        Assert.True(godScc.MemberIds.Count >= 4, $"Expected a large god SCC, got {godScc.MemberIds.Count} members.");

        // The planner must distribute that single SCC's members across >= 3 distinct migration units.
        var godMembers = new HashSet<string>(godScc.MemberIds, StringComparer.Ordinal);
        var owningUnits = _fx.Plan.Units
            .Where(u => u.MemberModuleIds.Any(godMembers.Contains))
            .ToList();

        Assert.True(owningUnits.Count >= 3,
            $"Expected the god cluster to be split into >= 3 candidate units, got {owningUnits.Count}: " +
            string.Join(", ", owningUnits.Select(u => u.ProposedServiceName)));
    }

    [Fact]
    public void ProposedBoundaries_ResolveToTheFourTargetServices()
    {
        var names = _fx.Plan.Units.Select(u => u.ProposedServiceName).ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[]
                 {
                     "RouteValidationService", "TotDeconflictionService",
                     "MissionDistributionService", "TaskingRulesService",
                 })
        {
            Assert.Contains(expected, names);
        }
    }

    // ── Risk scoring: every module in [0,1] ─────────────────────────────────────

    [Fact]
    public void EveryModule_GetsRiskScore_InUnitInterval()
    {
        // The planner scores modules internally; re-run the documented risk model over every module
        // and assert the composite is always in [0,1] (the contract for ModuleNode.RiskScore).
        var ruleCount = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in _fx.Report.BusinessRules)
            foreach (var refId in r.SourceRefs)
                ruleCount[refId] = ruleCount.GetValueOrDefault(refId) + 1;

        Assert.NotEmpty(_fx.Report.Modules);
        foreach (var m in _fx.Report.Modules)
        {
            double risk = RiskModel.Score(m, ruleCount.GetValueOrDefault(m.Id));
            Assert.InRange(risk, 0.0, 1.0);
        }
    }

    [Fact]
    public void RiskWeights_SumToOne()
    {
        Assert.Equal(1.0, RiskWeights.Sum, 9);
    }

    [Fact]
    public void EveryUnit_AggregateRisk_InUnitInterval()
    {
        Assert.NotEmpty(_fx.Plan.Units);
        foreach (var u in _fx.Plan.Units)
            Assert.InRange(u.AggregateRiskScore, 0.0, 1.0);
    }

    // ── Highest-risk unit contains the god method ───────────────────────────────

    [Fact]
    public void HighestRiskUnit_ContainsTheGodMethod()
    {
        var god = _fx.GodMethod;
        Assert.True(god.Complexity.CyclomaticComplexity > 30,
            $"Sanity: god method CC should be > 30, was {god.Complexity.CyclomaticComplexity}.");

        var highest = _fx.Plan.Units.OrderByDescending(u => u.AggregateRiskScore).First();
        Assert.Contains(god.Id, highest.MemberModuleIds);
    }

    // ── Ordering: OrderedUnitIds is a valid topological order ────────────────────

    [Fact]
    public void OrderedUnitIds_CoverEveryUnit_Exactly_Once()
    {
        var unitIds = _fx.Plan.Units.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(unitIds.Count, _fx.Plan.OrderedUnitIds.Count);
        Assert.Equal(unitIds, _fx.Plan.OrderedUnitIds.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public void OrderedUnitIds_AreAValidTopologicalOrder_NoEdgePointsBackward()
    {
        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < _fx.Plan.OrderedUnitIds.Count; i++)
            position[_fx.Plan.OrderedUnitIds[i]] = i;

        // Migration is dependency-FIRST: for an edge u -> v ("u depends on v"), v must be migrated
        // BEFORE u, i.e. v appears EARLIER in the order. No edge may point backward.
        foreach (var e in _fx.Plan.UnitEdges)
        {
            Assert.True(position.ContainsKey(e.FromId) && position.ContainsKey(e.ToId),
                $"Unit edge references an unknown unit: {e.FromId} -> {e.ToId}");
            Assert.True(position[e.ToId] < position[e.FromId],
                $"Topological order violated: dependency {e.ToId} (pos {position[e.ToId]}) must precede " +
                $"dependent {e.FromId} (pos {position[e.FromId]}).");
        }
    }

    [Fact]
    public void UnitEdges_FormADag_AndAreInterUnitOnly()
    {
        foreach (var e in _fx.Plan.UnitEdges)
        {
            Assert.NotEqual(e.FromId, e.ToId);                  // no self-loops
            Assert.DoesNotContain("external:database", e.FromId); // db sink excluded
            Assert.DoesNotContain("external:database", e.ToId);
        }
        // Acyclicity: a valid topological order exists (proved by the test above) => DAG. Here we
        // additionally assert there is no 2-cycle u<->v among the unit edges.
        var pairs = _fx.Plan.UnitEdges.Select(e => (e.FromId, e.ToId)).ToHashSet();
        foreach (var (from, to) in pairs)
            Assert.DoesNotContain((to, from), pairs);
    }

    // ── Mermaid ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Mermaid_IsNonEmpty_AndReferencesEveryUnitId()
    {
        var mermaid = _fx.Plan.MermaidDiagram;
        Assert.False(string.IsNullOrWhiteSpace(mermaid));
        Assert.Contains("flowchart", mermaid);
        foreach (var u in _fx.Plan.Units)
            Assert.Contains(u.Id, mermaid);  // each unit id appears in a node label
    }

    // ── JSON serialization helper ───────────────────────────────────────────────

    [Fact]
    public void Plan_SerializesToJson_WithExpectedFields()
    {
        string json = MigrationPlanJson.Serialize(_fx.Plan);
        Assert.Contains("\"Units\"", json);
        Assert.Contains("\"OrderedUnitIds\"", json);
        Assert.Contains("\"UnitEdges\"", json);
        Assert.Contains("\"MermaidDiagram\"", json);
        Assert.Contains("ProposedServiceName", json);
        // Round-trips through System.Text.Json without throwing.
        var back = System.Text.Json.JsonSerializer.Deserialize<MigrationPlan>(json);
        Assert.NotNull(back);
        Assert.Equal(_fx.Plan.Units.Count, back!.Units.Count);
    }

    [Fact]
    public void Plan_WritesTo_ResultsDirectory()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "forge-evolve-planner-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string path = MigrationPlanJson.WriteTo(Path.Combine(tmp, "migration-plan.json"), _fx.Plan);
            Assert.True(File.Exists(path));
            string content = File.ReadAllText(path);
            Assert.Contains("OrderedUnitIds", content);
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        }
    }
}
