// FORGE EVOLVE for TMPC — Governance workstream (WS-H).
//
// ProvenanceJson serializes the ledger to results/provenance.json — the artifact the README and
// companion cite, and the one the Cyber/cATO overlay consumes for CatoArtifacts.ProvenanceMerkleRoot.
//
// Shape (coordinated with the Contracts ProvenanceRecord so the Cyber module can read it back):
//   {
//     "schema": "forge-evolve/provenance/v1",
//     "merkleRoot": "<hex>",
//     "entryCount": <int>,
//     "records": [ { sequence, action, actor, payloadSha256, prevHash, entryHash }, ... ]
//   }
//
// Record properties are camelCased to match the System.Text.Json defaults used elsewhere in the
// pipeline's results/ artifacts; the property NAMES map 1:1 onto Contracts.ProvenanceRecord.

using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Governance;

/// <summary>Top-level document written to results/provenance.json.</summary>
public sealed record ProvenanceDocument
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = "forge-evolve/provenance/v1";

    [JsonPropertyName("merkleRoot")]
    public required string MerkleRoot { get; init; }

    [JsonPropertyName("entryCount")]
    public required int EntryCount { get; init; }

    [JsonPropertyName("records")]
    public required IReadOnlyList<ProvenanceRecord> Records { get; init; }
}

/// <summary>Serializes / deserializes the governance ledger to and from JSON.</summary>
public static class ProvenanceJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Build the in-memory document for a ledger (records + Merkle root).</summary>
    public static ProvenanceDocument BuildDocument(HashchainLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var records = ledger.Records;
        return new ProvenanceDocument
        {
            MerkleRoot = ledger.CurrentMerkleRoot(),
            EntryCount = records.Count,
            Records = records,
        };
    }

    /// <summary>Serialize a ledger to an indented JSON string.</summary>
    public static string Serialize(HashchainLedger ledger) =>
        JsonSerializer.Serialize(BuildDocument(ledger), Options);

    /// <summary>
    /// Write the ledger to <paramref name="path"/> (default results/provenance.json), creating
    /// parent directories as needed. Returns the absolute path written.
    /// </summary>
    public static string Write(HashchainLedger ledger, string path = "results/provenance.json")
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(fullPath, Serialize(ledger));
        return fullPath;
    }

    /// <summary>Parse a provenance document JSON string (used by consumers/tests).</summary>
    public static ProvenanceDocument Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<ProvenanceDocument>(json, Options)
               ?? throw new InvalidOperationException("Provenance JSON deserialized to null.");
    }
}
