// TranscriptStore — the Orchestrator's OFFLINE deterministic replay store.
//
// Offline mode replays a serialized TransformResult keyed by the SHA-256 of
// "Unit.Id|SourceLanguage|TargetStack". The index (fixtures/transcripts/index.json) maps that key
// to a transcript file (fixtures/transcripts/<name>.json) containing the serialized TransformResult
// (the emitted modern files). This keeps transformation runs reproducible and keyless/air-gapped.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Transformation;

/// <summary>One entry in fixtures/transcripts/index.json.</summary>
public sealed record TranscriptIndexEntry
{
    public required string Key { get; init; }       // SHA-256 of Unit.Id|SourceLanguage|TargetStack
    public required string UnitId { get; init; }
    public required string SourceLanguage { get; init; }
    public required string TargetStack { get; init; }
    public required string TranscriptFile { get; init; } // relative to fixtures/transcripts/
}

/// <summary>Loads/looks up offline transcripts. Stateless apart from the configured base dir.</summary>
public sealed class TranscriptStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _transcriptsDir;

    public TranscriptStore(string transcriptsDir) => _transcriptsDir = transcriptsDir;

    /// <summary>The offline replay key: SHA-256 of "Unit.Id|SourceLanguage|TargetStack" (lowercase hex).</summary>
    public static string ComputeKey(string unitId, SourceLanguage sourceLanguage, string targetStack)
    {
        string material = unitId + "|" + sourceLanguage + "|" + targetStack;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Read the index (empty if absent).</summary>
    public IReadOnlyList<TranscriptIndexEntry> ReadIndex()
    {
        string indexPath = Path.Combine(_transcriptsDir, "index.json");
        if (!File.Exists(indexPath)) return Array.Empty<TranscriptIndexEntry>();
        string json = File.ReadAllText(indexPath);
        return JsonSerializer.Deserialize<List<TranscriptIndexEntry>>(json, JsonOpts)
               ?? new List<TranscriptIndexEntry>();
    }

    /// <summary>Resolve and deserialize the transcript for a key, or null if not found.</summary>
    public TransformResult? TryLoad(string key)
    {
        foreach (TranscriptIndexEntry entry in ReadIndex())
        {
            if (!string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
            string path = Path.Combine(_transcriptsDir, entry.TranscriptFile);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<TransformResult>(File.ReadAllText(path), JsonOpts);
        }
        return null;
    }
}
