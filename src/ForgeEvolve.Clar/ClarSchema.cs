// FORGE EVOLVE for TMPC — frozen CLAR schema loader + validator.
//
// Loads clar-spec/CLAR.schema.json (the FROZEN contract) and validates CLAR JSON against
// it using JsonSchema.Net (JSON Schema draft 2020-12, which the schema declares). The
// schema is loaded from an embedded resource so Validate() works regardless of the
// current working directory; if the embedded copy is somehow unavailable we fall back to
// walking up the directory tree to the on-disk clar-spec/CLAR.schema.json.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace ForgeEvolve.Clar;

/// <summary>Loads and compiles the frozen CLAR JSON Schema and validates documents against it.</summary>
public static class ClarSchema
{
    private const string EmbeddedResourceName = "ForgeEvolve.Clar.CLAR.schema.json";

    private static readonly Lazy<JsonSchema> _schema = new(LoadSchema);

    /// <summary>The compiled frozen CLAR schema.</summary>
    public static JsonSchema Schema => _schema.Value;

    /// <summary>The raw JSON text of the frozen CLAR schema (as loaded).</summary>
    public static string SchemaJson { get; private set; } = "";

    /// <summary>
    /// Validate a CLAR document (JSON string) against the frozen schema. Returns a list of
    /// human-readable error strings; an empty list means the document is valid. Malformed
    /// JSON yields a single "JSON parse error" entry rather than throwing.
    /// </summary>
    public static IReadOnlyList<string> Validate(string clarDocumentJson)
    {
        if (string.IsNullOrWhiteSpace(clarDocumentJson))
            return new[] { "Document is empty or whitespace." };

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(clarDocumentJson);
        }
        catch (JsonException ex)
        {
            return new[] { $"JSON parse error: {ex.Message}" };
        }

        if (node is null)
            return new[] { "Document parsed to JSON null." };

        var options = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = false,
        };

        EvaluationResults results = Schema.Evaluate(node, options);
        if (results.IsValid)
            return Array.Empty<string>();

        var errors = new List<string>();
        CollectErrors(results, errors);

        // Defensive: if the result was invalid but no leaf carried a message, surface a
        // generic failure so the caller never sees "invalid but no errors".
        if (errors.Count == 0)
            errors.Add("Document failed schema validation (no detailed message available).");

        return errors;
    }

    private static void CollectErrors(EvaluationResults results, List<string> sink)
    {
        if (results.Errors is { Count: > 0 })
        {
            string location = results.InstanceLocation.ToString();
            string where = string.IsNullOrEmpty(location) ? "(root)" : location;
            foreach (var kvp in results.Errors)
                sink.Add($"{where}: {kvp.Value} [{kvp.Key}]");
        }

        foreach (var detail in results.Details)
            CollectErrors(detail, sink);
    }

    private static JsonSchema LoadSchema()
    {
        string json = ReadEmbedded() ?? ReadFromDisk()
            ?? throw new InvalidOperationException(
                "Could not locate the frozen CLAR schema (embedded resource " +
                $"'{EmbeddedResourceName}' missing and no clar-spec/CLAR.schema.json found " +
                "by walking up from the base directory).");
        SchemaJson = json;
        return JsonSchema.FromText(json);
    }

    private static string? ReadEmbedded()
    {
        Assembly asm = typeof(ClarSchema).Assembly;
        using Stream? stream = asm.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? ReadFromDisk()
    {
        // Walk up from the base directory looking for clar-spec/CLAR.schema.json.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "clar-spec", "CLAR.schema.json");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        return null;
    }
}
