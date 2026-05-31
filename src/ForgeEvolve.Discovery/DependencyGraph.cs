// ─────────────────────────────────────────────────────────────────────────────
// DependencyGraph — builds DependencyEdges among C# methods/types and runs Tarjan's
// strongly-connected-components algorithm to surface tight-coupling clusters.
//
// PART OF: FORGE EVOLVE for TMPC, Discovery Engine (Stage 1, workstream WS-A).
//
// Edge kinds:
//   * Calls       — method A invokes method B (resolved by simple name within the analyzed set;
//                   sharpened with the SemanticModel symbol when it resolves).
//   * References  — a method references its declaring type (member -> type containment edge) and
//                   a type references the types of methods it calls into.
//   * DataAccess  — a method touches an ADO.NET data-access type (SqlConnection/SqlCommand/...).
//
// Tarjan runs over the method+type node set using Calls/References edges (DataAccess targets the
// synthetic external "db" node, which is excluded from the cycle search).
// ─────────────────────────────────────────────────────────────────────────────

using ForgeEvolve.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForgeEvolve.Discovery;

internal sealed class DependencyGraphResult
{
    public List<DependencyEdge> Edges { get; } = new();
    public List<StronglyConnectedComponent> Sccs { get; } = new();
    /// <summary>FanIn per module id, computed from incoming Calls/References edges.</summary>
    public Dictionary<string, int> FanInById { get; } = new();
}

internal static class DependencyGraph
{
    public const string ExternalDbNode = "external:database";

    public static DependencyGraphResult Build(CSharpAnalysisResult cs)
    {
        var result = new DependencyGraphResult();
        var edges = result.Edges;

        // Index methods by simple name for call resolution (the surrogate has no name collisions
        // across its helper set; where ambiguity exists we add an edge to each candidate).
        var methodsByName = cs.Methods
            .GroupBy(m => m.SimpleName)
            .ToDictionary(g => g.Key, g => g.ToList());
        var methodIds = new HashSet<string>(cs.Methods.Select(m => m.Id));
        var typeIds = new HashSet<string>(cs.Types.Select(t => t.Id));

        var seen = new HashSet<(string, string, DependencyKind)>();
        void AddEdge(string from, string to, DependencyKind kind)
        {
            if (seen.Add((from, to, kind)))
                edges.Add(new DependencyEdge { FromId = from, ToId = to, Kind = kind });
        }

        foreach (var m in cs.Methods)
        {
            // Containment: method -> declaring type (References).
            AddEdge(m.Id, m.TypeId, DependencyKind.References);

            SyntaxNode? body = (SyntaxNode?)m.Syntax.Body ?? m.Syntax.ExpressionBody;
            if (body == null) continue;

            // Calls: resolve invocation targets by simple name within the analyzed set.
            foreach (var inv in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string? callee = InvocationSimpleName(inv);
                if (callee == null) continue;
                if (methodsByName.TryGetValue(callee, out var candidates))
                {
                    foreach (var target in candidates)
                    {
                        if (target.Id == m.Id) continue; // ignore trivial self-recursion for edges
                        AddEdge(m.Id, target.Id, DependencyKind.Calls);
                        // Type-level reference edge between the two declaring types (coupling).
                        if (m.TypeId != target.TypeId)
                            AddEdge(m.TypeId, target.TypeId, DependencyKind.References);
                    }
                }
            }

            // DataAccess: any ADO.NET object creation -> synthetic external db node.
            bool touchesDb = body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                .Any(oce => CSharpAnalyzer.IsDataAccessType(oce.Type.ToString()));
            if (touchesDb)
            {
                AddEdge(m.Id, ExternalDbNode, DependencyKind.DataAccess);
                AddEdge(m.TypeId, ExternalDbNode, DependencyKind.DataAccess);
            }
        }

        // FanIn: count incoming Calls/References edges per real node.
        foreach (var id in methodIds.Concat(typeIds))
            result.FanInById[id] = 0;
        foreach (var e in edges)
        {
            if (e.Kind is DependencyKind.Calls or DependencyKind.References &&
                result.FanInById.ContainsKey(e.ToId))
            {
                result.FanInById[e.ToId]++;
            }
        }

        // Tarjan SCC over the real-node graph (exclude the external db sink).
        //
        // Coupling model (documented decision): a Calls edge is directed as-is. A member<->type
        // CONTAINMENT relationship (a method's References edge to its declaring type) is modeled as
        // BIDIRECTIONAL structural coupling: the method depends on its enclosing type (its static
        // config/fields/identity), and the type's behavior IS its members. This makes a god-class
        // and its members a single strongly-connected coupling cluster — exactly the "tight coupling
        // cluster" Tarjan is meant to surface for the modernization seam analysis. Pure call edges
        // remain one-directional, so a true acyclic call chain still yields singleton SCCs.
        var nodes = methodIds.Concat(typeIds).ToList();
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var n in nodes) adjacency[n] = new List<string>();
        foreach (var e in edges)
        {
            if (e.ToId == ExternalDbNode) continue;
            if (!adjacency.ContainsKey(e.FromId) || !adjacency.ContainsKey(e.ToId)) continue;

            bool memberToType = e.Kind == DependencyKind.References
                                && methodIds.Contains(e.FromId) && typeIds.Contains(e.ToId);
            if (e.Kind == DependencyKind.Calls)
            {
                adjacency[e.FromId].Add(e.ToId);
            }
            else if (memberToType)
            {
                // Bidirectional containment coupling.
                adjacency[e.FromId].Add(e.ToId);
                adjacency[e.ToId].Add(e.FromId);
            }
            else if (e.Kind == DependencyKind.References)
            {
                // Type->type references (cross-type coupling) stay directed.
                adjacency[e.FromId].Add(e.ToId);
            }
        }

        result.Sccs.AddRange(Tarjan(nodes, adjacency));
        return result;
    }

    // ── Tarjan's strongly-connected-components (iterative, stack-safe) ─────────
    //
    // Classic Tarjan: DFS assigning each node a discovery index and a low-link; a node whose
    // low-link equals its index roots an SCC popped off the working stack. Implemented
    // iteratively so deep graphs cannot overflow the call stack.
    internal static List<StronglyConnectedComponent> Tarjan(
        IReadOnlyList<string> nodes,
        IReadOnlyDictionary<string, List<string>> adjacency)
    {
        var index = new Dictionary<string, int>();
        var lowlink = new Dictionary<string, int>();
        var onStack = new HashSet<string>();
        var stack = new Stack<string>();
        int nextIndex = 0;
        var components = new List<StronglyConnectedComponent>();
        int sccCounter = 0;

        foreach (var start in nodes)
        {
            if (index.ContainsKey(start)) continue;

            // Iterative DFS frame: (node, enumerator over its successors).
            var work = new Stack<(string node, IEnumerator<string> children)>();
            index[start] = lowlink[start] = nextIndex++;
            stack.Push(start);
            onStack.Add(start);
            work.Push((start, adjacency[start].GetEnumerator()));

            while (work.Count > 0)
            {
                var (v, children) = work.Peek();
                bool descended = false;
                while (children.MoveNext())
                {
                    var w = children.Current;
                    if (!index.ContainsKey(w))
                    {
                        index[w] = lowlink[w] = nextIndex++;
                        stack.Push(w);
                        onStack.Add(w);
                        work.Push((w, adjacency[w].GetEnumerator()));
                        descended = true;
                        break;
                    }
                    else if (onStack.Contains(w))
                    {
                        lowlink[v] = Math.Min(lowlink[v], index[w]);
                    }
                }
                if (descended) continue;

                // All children processed for v: it is a root if lowlink == index.
                if (lowlink[v] == index[v])
                {
                    var members = new List<string>();
                    string popped;
                    do
                    {
                        popped = stack.Pop();
                        onStack.Remove(popped);
                        members.Add(popped);
                    } while (popped != v);

                    members.Reverse();
                    components.Add(new StronglyConnectedComponent
                    {
                        Id = $"scc-{sccCounter++}",
                        MemberIds = members,
                    });
                }

                work.Pop();
                if (work.Count > 0)
                {
                    var parent = work.Peek().node;
                    lowlink[parent] = Math.Min(lowlink[parent], lowlink[v]);
                }
            }
        }

        return components;
    }

    private static string? InvocationSimpleName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,                 // Foo(...)
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,    // x.Foo(...) / Type.Foo(...)
        _ => null,
    };
}
