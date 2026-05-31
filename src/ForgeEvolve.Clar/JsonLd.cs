// FORGE EVOLVE for TMPC — JSON-LD @context helper.
//
// The CLAR schema accepts "@context" as either a string IRI or an inline object. We emit an
// inline object that (a) declares the vocabulary base and (b) maps the CLAR layer terms to
// IRIs under that base, so a consumer can interpret a CLAR document as Linked Data. Emitting
// the context inline (rather than just an IRI) keeps documents self-describing and avoids a
// network fetch during offline/air-gapped runs.

using System.Text.Json;
using ForgeEvolve.Clar.Model;

namespace ForgeEvolve.Clar;

/// <summary>Builds the JSON-LD <c>@context</c> for a CLAR document.</summary>
public static class JsonLd
{
    private const string Vocab = "https://577industries.com/forge-evolve/clar/v0.1.0/ns#";

    /// <summary>An inline JSON-LD context mapping CLAR terms to the published vocabulary.</summary>
    public static JsonElement ContextNode()
    {
        var ctx = new Dictionary<string, object>
        {
            ["@vocab"] = Vocab,
            ["clar"] = Vocab,
            ["controlFlow"] = $"{Vocab}controlFlow",
            ["dataFlow"] = $"{Vocab}dataFlow",
            ["businessLogic"] = $"{Vocab}businessLogic",
            ["infrastructure"] = $"{Vocab}infrastructure",
            ["precisionConstrained"] = $"{Vocab}precisionConstrained",
            ["clarType"] = $"{Vocab}clarType",
            ["ruleRef"] = new Dictionary<string, object>
            {
                ["@id"] = $"{Vocab}ruleRef",
                ["@type"] = "@id",
            },
            ["sourceModuleId"] = new Dictionary<string, object>
            {
                ["@id"] = $"{Vocab}sourceModuleId",
                ["@type"] = "@id",
            },
        };

        // Round-trip through System.Text.Json so the result is a detached JsonElement that
        // can be assigned to ClarDocument.Context and re-serialized verbatim.
        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(ctx, ClarSerialization.Options);
        using var parsed = JsonDocument.Parse(utf8);
        return parsed.RootElement.Clone();
    }
}
