using System.Reflection;
using System.Text.Json;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Orchestrator;

/// <summary>
/// Loads recorded <see cref="TransformResult"/> transcripts for Offline replay.
///
/// Each transcript is a serialized <see cref="TransformResult"/> JSON file under
/// <c>fixtures/transcripts/</c>. An <c>index.json</c> maps the deterministic transcript key
/// (see <see cref="TranscriptKey"/>) to its filename. The store resolves the filesystem copy
/// first (so the Transformation workstream can drop in new transcripts without a rebuild) and
/// falls back to the copies embedded in this assembly (so replay works from any working
/// directory, e.g. inside <c>dotnet test</c>).
/// </summary>
public sealed class TranscriptStore
{
    private const string IndexFileName = "index.json";
    private const string ResourcePrefix = "ForgeEvolve.Orchestrator.fixtures.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string? _fixturesDir;
    private readonly Assembly _assembly;
    private readonly IReadOnlyDictionary<string, string> _index;

    private TranscriptStore(string? fixturesDir, Assembly assembly, IReadOnlyDictionary<string, string> index)
    {
        _fixturesDir = fixturesDir;
        _assembly = assembly;
        _index = index;
    }

    /// <summary>The key→filename map loaded from index.json.</summary>
    public IReadOnlyDictionary<string, string> Index => _index;

    /// <summary>
    /// Open a store. <paramref name="fixturesDir"/> is the on-disk transcript directory; when null
    /// the store auto-discovers <c>fixtures/transcripts</c> by walking up from the assembly
    /// location and the current directory, and otherwise serves everything from embedded resources.
    /// </summary>
    public static TranscriptStore Open(string? fixturesDir = null, Assembly? assembly = null)
    {
        assembly ??= typeof(TranscriptStore).Assembly;
        var dir = fixturesDir ?? DiscoverFixturesDir(assembly);

        var index = LoadIndex(dir, assembly);
        return new TranscriptStore(dir, assembly, index);
    }

    /// <summary>
    /// Resolve a transcript by its deterministic key. Throws a clear, actionable error if the key
    /// is not in the index or its file is missing — the Offline path NEVER fabricates a result.
    /// </summary>
    public TransformResult Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_index.TryGetValue(key, out var fileName))
        {
            throw new TranscriptNotFoundException(
                $"No recorded transcript for key '{key}'. " +
                $"The Offline orchestrator replays recorded transcripts only and will not fabricate model output. " +
                $"Add the transcript JSON under fixtures/transcripts/ and map key→filename in {IndexFileName} " +
                $"(known keys: {(_index.Count == 0 ? "<none>" : string.Join(", ", _index.Keys))}).");
        }

        var json = ReadTranscript(fileName);
        var result = JsonSerializer.Deserialize<TransformResult>(json, JsonOptions)
            ?? throw new InvalidDataException(
                $"Transcript '{fileName}' for key '{key}' deserialized to null. The file is present but not a valid TransformResult.");

        return result;
    }

    /// <summary>True if a transcript is registered for the key (no deserialization).</summary>
    public bool Contains(string key) => _index.ContainsKey(key);

    // ── loading ────────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, string> LoadIndex(string? dir, Assembly assembly)
    {
        // Filesystem index wins when present (lets the Transformation WS add transcripts live).
        if (dir is not null)
        {
            var path = Path.Combine(dir, IndexFileName);
            if (File.Exists(path))
                return ParseIndex(File.ReadAllText(path));
        }

        // Embedded fallback.
        var resourceName = ResourcePrefix + IndexFileName;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is not null)
        {
            using var reader = new StreamReader(stream);
            return ParseIndex(reader.ReadToEnd());
        }

        throw new FileNotFoundException(
            $"Transcript index '{IndexFileName}' not found on disk (searched '{dir ?? "<none>"}') " +
            $"nor embedded as '{resourceName}'. The Offline orchestrator cannot replay without an index.");
    }

    private static Dictionary<string, string> ParseIndex(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
            ?? throw new InvalidDataException($"{IndexFileName} is empty or not a JSON object of key→filename.");
        // Keys are lowercase-hex SHA-256; normalize for safety.
        return raw.ToDictionary(kv => kv.Key.Trim().ToLowerInvariant(), kv => kv.Value.Trim());
    }

    private string ReadTranscript(string fileName)
    {
        if (_fixturesDir is not null)
        {
            var path = Path.Combine(_fixturesDir, fileName);
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        var resourceName = ResourcePrefix + fileName.Replace('/', '.').Replace('\\', '.');
        using var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream is not null)
        {
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        throw new TranscriptNotFoundException(
            $"Transcript file '{fileName}' is referenced by {IndexFileName} but was not found on disk " +
            $"(searched '{_fixturesDir ?? "<none>"}') nor embedded as '{resourceName}'.");
    }

    private static string? DiscoverFixturesDir(Assembly assembly)
    {
        const string rel = "fixtures/transcripts";

        foreach (var start in EnumerateStartDirs(assembly))
        {
            var dir = start;
            // Walk up looking for a directory that contains fixtures/transcripts/index.json.
            for (var i = 0; i < 12 && dir is not null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "src", "ForgeEvolve.Orchestrator", rel);
                if (File.Exists(Path.Combine(candidate, IndexFileName)))
                    return candidate;

                var direct = Path.Combine(dir.FullName, rel);
                if (File.Exists(Path.Combine(direct, IndexFileName)))
                    return direct;

                dir = dir.Parent;
            }
        }
        return null;
    }

    private static IEnumerable<DirectoryInfo> EnumerateStartDirs(Assembly assembly)
    {
        var asmDir = Path.GetDirectoryName(assembly.Location);
        if (!string.IsNullOrEmpty(asmDir))
            yield return new DirectoryInfo(asmDir);
        yield return new DirectoryInfo(Directory.GetCurrentDirectory());
    }
}

/// <summary>Thrown when an Offline replay key has no recorded transcript. Never fabricated output.</summary>
public sealed class TranscriptNotFoundException : Exception
{
    public TranscriptNotFoundException(string message) : base(message) { }
}
