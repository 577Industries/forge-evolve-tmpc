// FORGE EVOLVE for TMPC — shared CLAR serialization options.

using System.Text.Json;

namespace ForgeEvolve.Clar;

/// <summary>Shared System.Text.Json options used to emit CLAR documents.</summary>
public static class ClarSerialization
{
    /// <summary>
    /// Indented, non-escaping options. Property names are taken from the model's explicit
    /// [JsonPropertyName] attributes (which match the schema), so no naming policy is set.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
