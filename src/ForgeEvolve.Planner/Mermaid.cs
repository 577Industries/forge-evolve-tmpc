// ─────────────────────────────────────────────────────────────────────────────
// Mermaid — render a risk-annotated flowchart of the migration units + their seams.
//
// PART OF: FORGE EVOLVE for TMPC, Migration Planner (Stage 2, workstream WS-C).
//
// Produces a Mermaid `flowchart LR` string: one node per migration unit (labeled with the proposed
// service name, the unit id, and its aggregate risk), edges for the inter-unit dependencies
// (strangler-fig seams, labeled with the contracted weight), and a risk class (low/med/high) so the
// diagram is readable. The migration ORDER is shown as a numeric prefix on each node label.
// ─────────────────────────────────────────────────────────────────────────────

using System.Globalization;
using System.Text;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Planner;

internal static class Mermaid
{
    public static string Render(
        IReadOnlyList<MigrationUnit> units,
        IReadOnlyList<DependencyEdge> unitEdges,
        IReadOnlyList<string> ordered)
    {
        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    %% FORGE EVOLVE — Migration Plan (units = candidate microservices, edges = strangler-fig seams)");
        sb.AppendLine("    %% Node label: [order] ServiceName / risk=AggregateRiskScore. Heuristic proposal on the synthetic surrogate.");

        var orderIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < ordered.Count; i++) orderIndex[ordered[i]] = i + 1;

        foreach (var u in units)
        {
            string node = NodeId(u.Id);
            int ord = orderIndex.GetValueOrDefault(u.Id, 0);
            string risk = u.AggregateRiskScore.ToString("F3", CultureInfo.InvariantCulture);
            string label = $"{ord}. {u.ProposedServiceName}<br/>{u.Id}<br/>risk={risk}";
            sb.AppendLine($"    {node}[\"{Escape(label)}\"]");
        }

        sb.AppendLine();
        foreach (var e in unitEdges)
        {
            string from = NodeId(e.FromId);
            string to = NodeId(e.ToId);
            sb.AppendLine($"    {from} -->|\"depends ×{e.Weight}\"| {to}");
        }

        sb.AppendLine();
        sb.AppendLine("    classDef low fill:#1b5e20,stroke:#0b3d12,color:#fff;");
        sb.AppendLine("    classDef med fill:#e65100,stroke:#8c3100,color:#fff;");
        sb.AppendLine("    classDef high fill:#b71c1c,stroke:#7f1010,color:#fff;");
        foreach (var u in units)
        {
            string cls = u.AggregateRiskScore >= 0.66 ? "high" : u.AggregateRiskScore >= 0.40 ? "med" : "low";
            sb.AppendLine($"    class {NodeId(u.Id)} {cls};");
        }

        return sb.ToString().TrimEnd('\n', '\r') + "\n";
    }

    /// <summary>Mermaid node ids must be identifier-safe; derive a stable one from the unit id.</summary>
    private static string NodeId(string unitId)
    {
        var chars = unitId.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        string s = new string(chars);
        return char.IsLetter(s[0]) ? s : "u_" + s;
    }

    private static string Escape(string s) => s.Replace("\"", "&quot;");
}
