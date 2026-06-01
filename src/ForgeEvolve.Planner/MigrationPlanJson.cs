// ─────────────────────────────────────────────────────────────────────────────
// MigrationPlanJson — System.Text.Json serialization helper for the migration plan.
//
// PART OF: FORGE EVOLVE for TMPC, Migration Planner (Stage 2, workstream WS-C).
//
// Produces the deterministic, reviewer-diffable JSON written to results/migration-plan.json. Uses
// indented output and the contract's JsonStringEnumConverter attributes (already on the enums) for
// human-readable enums.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Planner;

public static class MigrationPlanJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(MigrationPlan plan) => JsonSerializer.Serialize(plan, Options);

    /// <summary>
    /// Write the plan JSON to a path (default: results/migration-plan.json under the given root),
    /// creating parent directories as needed. Returns the absolute path written.
    /// </summary>
    public static string WriteTo(string path, MigrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(full, Serialize(plan));
        return full;
    }

    /// <summary>Convenience: write to &lt;repoRoot&gt;/results/migration-plan.json.</summary>
    public static string WriteToResults(string repoRoot, MigrationPlan plan) =>
        WriteTo(Path.Combine(repoRoot, "results", "migration-plan.json"), plan);
}
