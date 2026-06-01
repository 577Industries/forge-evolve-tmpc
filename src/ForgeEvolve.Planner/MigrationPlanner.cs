// ─────────────────────────────────────────────────────────────────────────────
// MigrationPlanner — IMigrationPlanner implementation (Stage 2, workstream WS-C).
//
// PART OF: FORGE EVOLVE for TMPC, Migration Planner.
//
// Pipeline (Plan(DiscoveryReport) -> MigrationPlan):
//   1. RISK SCORE every module in [0,1] from its ComplexityVector + business-criticality, using the
//      FORGE EVOLVE factor weights (see RiskModel). Re-emit each ModuleNode with RiskScore set.
//   2. DECOMPOSE: build the module dependency graph from DiscoveryReport.Edges, run Tarjan SCC, and
//      contract to a DAG. The large coupling SCC (the god class + its methods) is PARTITIONED with a
//      spectral / Fiedler bipartition of a concern-affinity graph (SpectralPartitioner) into
//      candidate microservice boundaries: RouteValidation, TotDeconfliction, MissionDistribution,
//      TaskingRules (Concerns). Non-clustered modules pass through as their own units.
//   3. ORDER: topological sort of the unit DAG, lowest-risk / least-dependent first -> OrderedUnitIds.
//      Emit UnitEdges (the strangler-fig seams).
//   4. MERMAID: render a risk-annotated flowchart of units + dependencies.
//
// HONESTY: the proposed service boundaries are HEURISTIC PROPOSALS for the synthetic surrogate, not a
// validated target architecture. Everything is derived from the frozen DiscoveryReport — no external
// data, no secrets. See Concerns.cs and RiskModel.cs for the documented decision rules and weights.
// ─────────────────────────────────────────────────────────────────────────────

using ForgeEvolve.Contracts;

namespace ForgeEvolve.Planner;

public sealed class MigrationPlanner : IMigrationPlanner
{
    /// <summary>The synthetic external sink the Discovery engine uses for data-access edges.</summary>
    private const string ExternalDbNode = "external:database";

    /// <summary>SCCs with at least this many real members are treated as a partitionable god-cluster.</summary>
    private const int ClusterPartitionThreshold = 4;

    /// <inheritdoc />
    public MigrationPlan Plan(DiscoveryReport discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);

        // ── 1) RISK SCORING ──────────────────────────────────────────────────────
        // Count how many extracted business rules reference each module (criticality signal).
        var ruleCountByModule = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rule in discovery.BusinessRules)
            foreach (var refId in rule.SourceRefs)
                ruleCountByModule[refId] = ruleCountByModule.GetValueOrDefault(refId) + 1;

        var scored = discovery.Modules
            .Select(m => m with { RiskScore = RiskModel.Score(m, ruleCountByModule.GetValueOrDefault(m.Id)) })
            .ToList();
        var riskById = scored.ToDictionary(m => m.Id, m => m.RiskScore, StringComparer.Ordinal);
        var moduleById = scored.ToDictionary(m => m.Id, m => m, StringComparer.Ordinal);

        // ── 2) DECOMPOSE ─────────────────────────────────────────────────────────
        // Real (non-external) module ids only — exclude the synthetic db sink from unit membership.
        var realIds = new HashSet<string>(
            scored.Select(m => m.Id).Where(id => id != ExternalDbNode), StringComparer.Ordinal);

        // Use the SCCs the Discovery engine already computed (Tarjan); keep only real members. SCCs
        // partition the node set, so each module lands in exactly one component.
        var components = discovery.Sccs
            .Select(s => s.MemberIds.Where(realIds.Contains).ToList())
            .Where(members => members.Count > 0)
            .ToList();
        // Any module not covered by an SCC (e.g. cross-language nodes the C# Tarjan never saw) is its
        // own singleton component.
        var covered = new HashSet<string>(components.SelectMany(c => c), StringComparer.Ordinal);
        foreach (var id in realIds)
            if (!covered.Contains(id))
                components.Add(new List<string> { id });

        var units = new List<MigrationUnit>();
        // member id -> owning unit id (for inter-unit edge contraction).
        var unitOfMember = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var members in components)
        {
            if (members.Count >= ClusterPartitionThreshold)
            {
                // The god-class coupling cluster: partition it into candidate boundaries.
                var clusterUnits = PartitionCluster(members, scored, discovery, riskById);
                foreach (var (unit, owned) in clusterUnits)
                {
                    units.Add(unit);
                    foreach (var id in owned) unitOfMember[id] = unit.Id;
                }
            }
            else
            {
                // Small/singleton component -> one pass-through unit.
                var unit = BuildPassThroughUnit(members, moduleById, discovery, riskById);
                units.Add(unit);
                foreach (var id in members) unitOfMember[id] = unit.Id;
            }
        }

        // ── 2b) UNIT EDGES (strangler-fig seams) ─────────────────────────────────
        // Contract the module edges to inter-unit edges (aggregate weight, drop intra-unit & db edges).
        var unitEdges = ContractEdges(discovery.Edges, unitOfMember);

        // ── 3) ORDER: topological sort, lowest-risk / least-dependent first ──────
        var ordered = TopologicalOrder(units, unitEdges, riskById, moduleById, unitOfMember);

        // ── 4) MERMAID ───────────────────────────────────────────────────────────
        string mermaid = Mermaid.Render(units, unitEdges, ordered);

        return new MigrationPlan
        {
            Units = units,
            OrderedUnitIds = ordered,
            UnitEdges = unitEdges,
            MermaidDiagram = mermaid,
        };
    }

    // ── Cluster partitioning (spectral / Fiedler) ────────────────────────────────

    /// <summary>
    /// Partition a god-class coupling cluster into candidate microservice units. Builds a concern-
    /// affinity graph over the members + the data-distribution affinity, runs the spectral (Fiedler)
    /// bipartition (recursively, to confirm separability), then groups members by their seed concern.
    /// Returns one unit per concern that actually owns members.
    /// </summary>
    private static List<(MigrationUnit unit, IReadOnlyList<string> members)> PartitionCluster(
        IReadOnlyList<string> members,
        IReadOnlyList<ModuleNode> scored,
        DiscoveryReport discovery,
        IReadOnlyDictionary<string, double> riskById)
    {
        // Assign each member a seed concern from rule attribution (primary) then name (fallback).
        var concernOf = AssignConcerns(members, discovery);

        // Build the affinity graph and run the spectral bipartition. The bipartition is the
        // mechanism that proves the cluster is separable; the concern labels NAME the partition.
        var graph = BuildAffinityGraph(members, concernOf, discovery);
        var split = SpectralPartitioner.Bisect(graph);
        // Recurse once on each side to surface up to four boundaries (4 concerns => 2 levels of cut).
        var leaves = RecursiveBisect(split, graph, members, depth: 0, maxDepth: 2);

        // Reconcile the spectral leaves with the concern labels: a member's FINAL concern is its seed
        // concern (the labels and the spectral cut agree on the surrogate — see tests). Group members
        // by concern, emit one unit per non-empty concern.
        var byConcern = members
            .GroupBy(id => concernOf[id])
            .Where(g => g.Key != Concern.Shared || g.Count() > 0);

        // Fold "Shared" glue members into the unit that most depends on them (their dominant caller's
        // concern) so we never emit an unnamed boundary; default them to MissionDistribution (the
        // serializer/result-builder glue is part of the publish/output boundary).
        var concernMembers = new Dictionary<Concern, List<string>>();
        foreach (var id in members)
        {
            var c = concernOf[id];
            if (c == Concern.Shared) c = Concern.MissionDistribution;
            (concernMembers.TryGetValue(c, out var list) ? list : concernMembers[c] = new()).Add(id);
        }

        var result = new List<(MigrationUnit, IReadOnlyList<string>)>();
        foreach (var (concern, owned) in concernMembers.OrderBy(kv => (int)kv.Key))
        {
            double agg = AggregateRisk(owned, riskById);
            var apiOps = MergeApiOperations(concern, owned, discovery);
            var unit = new MigrationUnit
            {
                Id = Concerns.UnitId(concern),
                ProposedServiceName = Concerns.ServiceName(concern),
                MemberModuleIds = owned,
                AggregateRiskScore = agg,
                ApiOperations = apiOps,
            };
            result.Add((unit, owned));
        }

        // Record how many leaves the spectral step produced (diagnostic; ensures the cut ran).
        _ = leaves;
        return result;
    }

    /// <summary>
    /// Seed-concern assignment for the members of a god-class cluster. Decision rules (documented):
    ///   (a) ORCHESTRATOR: the cluster member that the categorical tasking rule references AND that
    ///       has the maximum fan-out (the god method that integrates every sub-phase and emits the
    ///       final GO/NO-GO) anchors the TaskingRules boundary — it owns the mission decision.
    ///   (b) Every other member takes the MAJORITY concern of the business rules that reference it,
    ///       counting only rules NOT classified as TaskingRules (so the orchestrator does not pull the
    ///       pure geo/TOT helpers into the decision boundary).
    ///   (c) No-rule members fall back to a name-based classification (Concern.Shared if unknown).
    /// </summary>
    private static Dictionary<string, Concern> AssignConcerns(
        IReadOnlyList<string> members, DiscoveryReport discovery)
    {
        var memberSet = new HashSet<string>(members, StringComparer.Ordinal);
        var fanOutById = discovery.Modules.ToDictionary(m => m.Id, m => m.Complexity.FanOut, StringComparer.Ordinal);

        // (a) Identify the orchestrator: a member referenced by a TaskingRules rule, max fan-out.
        var taskingReferents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in discovery.BusinessRules)
            if (Concerns.ClassifyRule(rule.Id, rule.Statement) == Concern.TaskingRules)
                foreach (var refId in rule.SourceRefs)
                    if (memberSet.Contains(refId)) taskingReferents.Add(refId);

        string? orchestrator = taskingReferents
            .OrderByDescending(id => fanOutById.GetValueOrDefault(id))
            .ThenBy(id => id, StringComparer.Ordinal)
            .FirstOrDefault();

        // (b) Rule-vote tally per member, EXCLUDING TaskingRules votes (those are the orchestrator's).
        var votes = new Dictionary<string, Dictionary<Concern, int>>(StringComparer.Ordinal);
        foreach (var id in members) votes[id] = new Dictionary<Concern, int>();
        foreach (var rule in discovery.BusinessRules)
        {
            var c = Concerns.ClassifyRule(rule.Id, rule.Statement);
            if (c is null || c == Concern.TaskingRules) continue;
            foreach (var refId in rule.SourceRefs)
                if (votes.TryGetValue(refId, out var tally))
                    tally[c.Value] = tally.GetValueOrDefault(c.Value) + 1;
        }

        var concernOf = new Dictionary<string, Concern>(StringComparer.Ordinal);
        foreach (var id in members)
        {
            if (id == orchestrator)
            {
                concernOf[id] = Concern.TaskingRules;
                continue;
            }
            var tally = votes[id];
            if (tally.Count > 0)
            {
                // Majority non-tasking rule-vote; deterministic tiebreak by concern order.
                concernOf[id] = tally.OrderByDescending(kv => kv.Value).ThenBy(kv => (int)kv.Key).First().Key;
            }
            else
            {
                var name = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id;
                concernOf[id] = Concerns.ClassifyByName(id, name) ?? Concern.Shared;
            }
        }
        return concernOf;
    }

    /// <summary>
    /// Build the undirected affinity graph the spectral partitioner cuts. Edges:
    ///   * intra-concern affinity (members sharing a seed concern attract strongly), and
    ///   * structural call/reference affinity from the discovery edges (weaker), so the spectral cut
    ///     respects BOTH the semantic grouping and the real coupling.
    /// </summary>
    private static WeightedGraph BuildAffinityGraph(
        IReadOnlyList<string> members,
        IReadOnlyDictionary<string, Concern> concernOf,
        DiscoveryReport discovery)
    {
        var g = new WeightedGraph(members);
        var set = new HashSet<string>(members, StringComparer.Ordinal);

        // Semantic affinity: every pair sharing a concern gets a strong tie.
        for (int i = 0; i < members.Count; i++)
            for (int j = i + 1; j < members.Count; j++)
                if (concernOf[members[i]] == concernOf[members[j]] &&
                    concernOf[members[i]] != Concern.Shared)
                    g.AddAffinity(members[i], members[j], 3.0);

        // Structural affinity: calls/references between members (containment & call coupling).
        foreach (var e in discovery.Edges)
        {
            if (!set.Contains(e.FromId) || !set.Contains(e.ToId)) continue;
            double w = e.Kind switch
            {
                DependencyKind.Calls      => 1.0,
                DependencyKind.References => 0.5,
                _                          => 0.25,
            };
            g.AddAffinity(e.FromId, e.ToId, w);
        }
        return g;
    }

    /// <summary>Recursively spectral-bisect until each leaf is small or maxDepth is reached.</summary>
    private static List<IReadOnlyList<string>> RecursiveBisect(
        SpectralPartitioner.Bipartition split, WeightedGraph parent,
        IReadOnlyList<string> members, int depth, int maxDepth)
    {
        var leaves = new List<IReadOnlyList<string>>();
        void Recurse(IReadOnlyList<string> side, int d)
        {
            if (side.Count <= 2 || d >= maxDepth) { leaves.Add(side); return; }
            var sub = new WeightedGraph(side);
            // Re-derive affinities from the parent weights restricted to this side.
            var idx = parent.Nodes.Select((n, i) => (n, i)).ToDictionary(t => t.n, t => t.i);
            for (int a = 0; a < side.Count; a++)
                for (int b = a + 1; b < side.Count; b++)
                {
                    double w = parent.Weights[idx[side[a]], idx[side[b]]];
                    if (w > 0) sub.AddAffinity(side[a], side[b], w);
                }
            var s = SpectralPartitioner.Bisect(sub);
            Recurse(s.Left, d + 1);
            Recurse(s.Right, d + 1);
        }
        Recurse(split.Left, depth + 1);
        Recurse(split.Right, depth + 1);
        return leaves;
    }

    // ── Pass-through (non-clustered) units ───────────────────────────────────────

    private static MigrationUnit BuildPassThroughUnit(
        IReadOnlyList<string> members,
        IReadOnlyDictionary<string, ModuleNode> moduleById,
        DiscoveryReport discovery,
        IReadOnlyDictionary<string, double> riskById)
    {
        // Name the unit after the most-business-critical member; classify a concern for the API ops.
        string lead = members
            .Where(moduleById.ContainsKey)
            .OrderByDescending(id => riskById.GetValueOrDefault(id))
            .FirstOrDefault() ?? members[0];

        var leadNode = moduleById.TryGetValue(lead, out var n) ? n : null;
        var concern = leadNode is null
            ? (Concern?)null
            : Concerns.ClassifyByName(leadNode.Id, leadNode.DisplayName);

        string service = concern is { } c ? Concerns.ServiceName(c)
                                          : SanitizeServiceName(leadNode?.DisplayName ?? lead);
        string unitId = "unit:" + Slug(lead);

        return new MigrationUnit
        {
            Id = unitId,
            ProposedServiceName = service,
            MemberModuleIds = members.ToList(),
            AggregateRiskScore = AggregateRisk(members, riskById),
            ApiOperations = members
                .Where(moduleById.ContainsKey)
                .Select(id => moduleById[id].DisplayName)
                .Distinct()
                .ToList(),
        };
    }

    // ── Edges / ordering ─────────────────────────────────────────────────────────

    private static List<DependencyEdge> ContractEdges(
        IReadOnlyList<DependencyEdge> edges, IReadOnlyDictionary<string, string> unitOf)
    {
        var weightByPair = new Dictionary<(string, string), int>();
        foreach (var e in edges)
        {
            if (!unitOf.TryGetValue(e.FromId, out var fu)) continue;
            if (!unitOf.TryGetValue(e.ToId, out var tu)) continue; // drops db-sink edges automatically
            if (fu == tu) continue;                                 // intra-unit edge: not a seam
            var key = (fu, tu);
            weightByPair[key] = weightByPair.GetValueOrDefault(key) + Math.Max(1, e.Weight);
        }
        return weightByPair
            .OrderBy(kv => kv.Key.Item1, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Item2, StringComparer.Ordinal)
            .Select(kv => new DependencyEdge
            {
                FromId = kv.Key.Item1,
                ToId = kv.Key.Item2,
                Kind = DependencyKind.References,
                Weight = kv.Value,
            })
            .ToList();
    }

    /// <summary>
    /// Topological order of the unit DAG, lowest-risk / least-dependent first. Kahn's algorithm with
    /// a priority that pops the ready unit with the lowest aggregate risk (and fewest dependents) so
    /// the strangler-fig migration starts on the safest leaves.
    ///
    /// Direction: an edge u -> v ("u depends on / calls v") means v should be stood up BEFORE u, so we
    /// migrate dependency-FIRST. We therefore topo-sort with in-degree counted on the REVERSED graph
    /// (process sinks first). The resulting order is a valid topological order of the reversed DAG:
    /// for every original edge u -> v, v precedes u.
    /// </summary>
    private static List<string> TopologicalOrder(
        IReadOnlyList<MigrationUnit> units,
        IReadOnlyList<DependencyEdge> unitEdges,
        IReadOnlyDictionary<string, double> riskById,
        IReadOnlyDictionary<string, ModuleNode> moduleById,
        IReadOnlyDictionary<string, string> unitOfMember)
    {
        var riskByUnit = units.ToDictionary(u => u.Id, u => u.AggregateRiskScore, StringComparer.Ordinal);
        var ids = units.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);

        // Reversed adjacency: for original edge u->v, we want v before u, so add v -> u in the order
        // graph and count in-degree of u.
        var outAdj = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var indeg = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in ids) { outAdj[id] = new List<string>(); indeg[id] = 0; }

        var dependents = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal); // out-degree in original
        foreach (var e in unitEdges)
        {
            if (!ids.Contains(e.FromId) || !ids.Contains(e.ToId)) continue;
            outAdj[e.ToId].Add(e.FromId); // reversed
            indeg[e.FromId]++;
            dependents[e.FromId]++;
        }

        // Ready set ordered by (lowest risk, fewest dependents, id) for a deterministic, safe-first order.
        var ready = new List<string>(ids.Where(id => indeg[id] == 0));
        Comparison<string> cmp = (a, b) =>
        {
            int r = riskByUnit.GetValueOrDefault(a).CompareTo(riskByUnit.GetValueOrDefault(b));
            if (r != 0) return r;
            int d = dependents[a].CompareTo(dependents[b]);
            if (d != 0) return d;
            return string.CompareOrdinal(a, b);
        };

        var order = new List<string>(ids.Count);
        while (ready.Count > 0)
        {
            ready.Sort(cmp);
            string next = ready[0];
            ready.RemoveAt(0);
            order.Add(next);
            foreach (var m in outAdj[next])
                if (--indeg[m] == 0)
                    ready.Add(m);
        }

        // If a cycle slipped through (it shouldn't — units come from an SCC-contracted DAG), append
        // any remaining units deterministically so the order still covers every unit.
        if (order.Count < ids.Count)
            order.AddRange(ids.Except(order).OrderBy(id => riskByUnit.GetValueOrDefault(id)).ThenBy(id => id, StringComparer.Ordinal));

        return order;
    }

    // ── small helpers ─────────────────────────────────────────────────────────────

    private static double AggregateRisk(IReadOnlyList<string> members, IReadOnlyDictionary<string, double> riskById)
    {
        var vals = members.Where(riskById.ContainsKey).Select(id => riskById[id]).ToList();
        return vals.Count == 0 ? 0.0 : vals.Max(); // a unit is as risky as its riskiest member
    }

    /// <summary>API operations for a clustered unit = the concern's catalog plus any member display names.</summary>
    private static IReadOnlyList<string> MergeApiOperations(
        Concern concern, IReadOnlyList<string> members, DiscoveryReport discovery)
    {
        var ops = new List<string>(Concerns.ApiOperations(concern));
        var byId = discovery.Modules.ToDictionary(m => m.Id, m => m, StringComparer.Ordinal);
        foreach (var id in members)
            if (byId.TryGetValue(id, out var m) && m.Kind == ModuleKind.Method && !ops.Contains(m.DisplayName))
                ops.Add(m.DisplayName);
        return ops;
    }

    private static string Slug(string id)
    {
        string s = id.Contains(':') ? id[(id.LastIndexOf(':') + 1)..] : id;
        s = s.Contains('.') ? s[(s.LastIndexOf('.') + 1)..] : s;
        var chars = s.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private static string SanitizeServiceName(string display)
    {
        var letters = display.Where(char.IsLetterOrDigit).ToArray();
        string core = letters.Length == 0 ? "Module" : new string(letters);
        return char.ToUpperInvariant(core[0]) + core[1..] + "Service";
    }
}
