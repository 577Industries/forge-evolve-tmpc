// FORGE EVOLVE for TMPC — EquivalenceReport serializer.
//
// Writes the frozen EquivalenceReport DTO to results/equivalence-report.json (the artifact the
// proposal cites). Uses System.Text.Json defaults (the contract is designed for them):
// indented, camelCase, enums as strings.

using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Validation;

/// <summary>Serializes an <see cref="EquivalenceReport"/> to JSON.</summary>
public static class EquivalenceReportJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serialize a report to a JSON string.</summary>
    public static string Serialize(EquivalenceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, Options);
    }

    /// <summary>
    /// Serialize and write to <paramref name="path"/> (default
    /// <c>results/equivalence-report.json</c>), creating the directory if needed. Returns the
    /// absolute path written.
    /// </summary>
    public static string Write(EquivalenceReport report, string path = "results/equivalence-report.json")
    {
        ArgumentNullException.ThrowIfNull(report);
        string full = Path.GetFullPath(path);
        string? dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(full, Serialize(report));
        return full;
    }
}
