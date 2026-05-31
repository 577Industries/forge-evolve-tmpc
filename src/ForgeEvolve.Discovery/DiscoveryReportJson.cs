// ─────────────────────────────────────────────────────────────────────────────
// DiscoveryReportJson — System.Text.Json serialization helpers.
//
// PART OF: FORGE EVOLVE for TMPC, Discovery Engine (Stage 1, workstream WS-A).
//
// Produces the deterministic JSON later written to results/discovery-report.json, plus a small
// companion F1 governance object. Uses indented output and the contract's JsonStringEnumConverter
// attributes (already on the enums) for human-readable, reviewer-diffable artifacts.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Discovery;

public static class DiscoveryReportJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Records expose get/init properties; the default resolver serializes them fine. Enum
        // converters are declared on the contract enums themselves.
    };

    public static string Serialize(DiscoveryReport report) =>
        JsonSerializer.Serialize(report, Options);

    public static string Serialize(RuleF1Report f1) =>
        JsonSerializer.Serialize(f1, Options);

    /// <summary>Serialize the discovery report and the F1 governance metric as one bundle.</summary>
    public static string SerializeBundle(DiscoveryReport report, RuleF1Report f1)
    {
        var bundle = new
        {
            discovery = report,
            ruleExtractionF1 = f1,
        };
        return JsonSerializer.Serialize(bundle, Options);
    }

    /// <summary>Write the report JSON to a path, creating parent directories as needed.</summary>
    public static void WriteTo(string path, DiscoveryReport report)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Serialize(report));
    }
}
