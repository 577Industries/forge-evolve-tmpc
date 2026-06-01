// FORGE EVOLVE for TMPC — frozen golden-corpus loader.
//
// Loads surrogate/corpus/corpus.json (2000 vectors) and maps each entry onto the frozen
// EquivalenceTestVector contract. Mapping decision:
//
//   * InputJson           <- corpus `input`           (the MissionRequest)
//   * ExpectedOutputJson  <- corpus `referenceOutput` (the CORRECT answer key; used by the
//                            oracles to classify intentional divergence: legacy-wrong / modern-
//                            matches-reference-right)
//   * Tags                <- corpus `tags`
//
// The corpus also stores `legacyOutput` (the frozen buggy answer key) and the boolean
// `expectedLegacyDivergent` ground-truth label; the loader exposes those as parallel arrays so
// tests can build a legacyOutput-as-modern stand-in and score the divergence detector against
// the 321-vector ground truth — WITHOUT mutating the frozen EquivalenceTestVector contract.

using System.Text.Json;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Validation;

/// <summary>A fully-loaded corpus: the contract vectors plus the corpus-only side arrays.</summary>
public sealed class LoadedCorpus
{
    /// <summary>The frozen-contract vectors (Input + reference ExpectedOutput + Tags).</summary>
    public required IReadOnlyList<EquivalenceTestVector> Vectors { get; init; }
    /// <summary>The frozen buggy legacy answer key per vector (raw JSON), parallel to <see cref="Vectors"/>.</summary>
    public required IReadOnlyList<string> LegacyOutputs { get; init; }
    /// <summary>The corpus `expectedLegacyDivergent` ground-truth label, parallel to <see cref="Vectors"/>.</summary>
    public required IReadOnlyList<bool> ExpectedLegacyDivergent { get; init; }

    /// <summary>Count of ground-truth-divergent vectors (321 in the frozen corpus).</summary>
    public int DivergentCount
    {
        get { int n = 0; foreach (var b in ExpectedLegacyDivergent) if (b) n++; return n; }
    }
}

/// <summary>Reads and maps the frozen golden corpus.</summary>
public static class CorpusLoader
{
    /// <summary>Load from a corpus.json path.</summary>
    public static LoadedCorpus Load(string corpusJsonPath)
    {
        ArgumentNullException.ThrowIfNull(corpusJsonPath);
        if (!File.Exists(corpusJsonPath))
            throw new FileNotFoundException("corpus.json not found", corpusJsonPath);
        return Parse(File.ReadAllText(corpusJsonPath));
    }

    /// <summary>Parse corpus JSON text.</summary>
    public static LoadedCorpus Parse(string corpusJson)
    {
        using var doc = JsonDocument.Parse(corpusJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new FormatException("corpus root must be a JSON array.");

        var vectors = new List<EquivalenceTestVector>();
        var legacyOutputs = new List<string>();
        var divergent = new List<bool>();

        foreach (var vec in doc.RootElement.EnumerateArray())
        {
            string id = vec.GetProperty("id").GetString() ?? "";
            string inputJson = vec.GetProperty("input").GetRawText();
            string referenceJson = vec.GetProperty("referenceOutput").GetRawText();
            string legacyJson = vec.GetProperty("legacyOutput").GetRawText();

            var tags = new List<string>();
            if (vec.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
                foreach (var t in tagsEl.EnumerateArray())
                    tags.Add(t.GetString() ?? "");

            bool expDiv = vec.TryGetProperty("expectedLegacyDivergent", out var dEl)
                          && (dEl.ValueKind == JsonValueKind.True
                              || dEl.ValueKind == JsonValueKind.False)
                          && dEl.GetBoolean();

            vectors.Add(new EquivalenceTestVector
            {
                Id = id,
                InputJson = inputJson,
                ExpectedOutputJson = referenceJson, // the CORRECT reference answer key
                Tags = tags,
            });
            legacyOutputs.Add(legacyJson);
            divergent.Add(expDiv);
        }

        return new LoadedCorpus
        {
            Vectors = vectors,
            LegacyOutputs = legacyOutputs,
            ExpectedLegacyDivergent = divergent,
        };
    }

    /// <summary>
    /// Walk up from a starting directory to locate surrogate/corpus/corpus.json (mirrors the
    /// LegacyCheck tool's search so tests work regardless of the test runner's working dir).
    /// </summary>
    public static string? FindCorpus(string? startDir = null)
    {
        string? dir = startDir ?? AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            string c1 = Path.Combine(dir, "corpus", "corpus.json");
            if (File.Exists(c1)) return c1;
            string c2 = Path.Combine(dir, "surrogate", "corpus", "corpus.json");
            if (File.Exists(c2)) return c2;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
