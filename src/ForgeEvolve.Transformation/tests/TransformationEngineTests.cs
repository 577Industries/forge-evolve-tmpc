// Tests for the Transformation Engine (Stage 3).
//
// Asserts:
//   1. TransformAsync returns a TransformResult with non-empty Files and CompiledClean=true.
//   2. The emitted modern code's max method cyclomatic complexity is < 10 (pre-registered gate).
//   3. The transcript fixture round-trips (serialize -> deserialize -> equal Files/AgentId/Notes),
//      and the offline replay key matches the index entry.

using System.Text.Json;
using ForgeEvolve.Contracts;
using Xunit;

namespace ForgeEvolve.Transformation.Tests;

public sealed class TransformationEngineTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static TransformTask BuildTask() => new()
    {
        TaskId = "WS-E-test",
        Unit = new MigrationUnit
        {
            Id = "MissionRouting.MissionProcessor",
            ProposedServiceName = "MissionService",
            MemberModuleIds = new[] { "MissionRouting.MissionProcessor.ProcessMission" },
            AggregateRiskScore = 0.0,
            ApiOperations = new[] { "ProcessMission" },
        },
        ClarDocumentJson = "{}",
        Rules = Array.Empty<BusinessRule>(),
        SourceLanguage = SourceLanguage.CSharp,
        TargetStack = "dotnet8",
    };

    private static string RepoRoot() => RepoLocator.Locate();

    [Fact]
    public async Task TransformAsync_returns_nonempty_files_and_compiled_clean()
    {
        var engine = new TransformationEngine(RepoRoot());

        TransformResult result = await engine.TransformAsync(BuildTask());

        Assert.NotNull(result);
        Assert.NotEmpty(result.Files);
        Assert.True(result.CompiledClean, "modern component compiles clean (TreatWarningsAsErrors).");
        Assert.All(result.Files, f => Assert.False(string.IsNullOrWhiteSpace(f.Content)));
        Assert.All(result.Files, f => Assert.Equal(SourceLanguage.CSharp, f.Language));
        Assert.Equal("offline-replay", result.AgentId);
        Assert.Equal(OrchestratorMode.Offline, result.Mode);
    }

    [Fact]
    public async Task Emitted_modern_code_max_method_cc_below_ten()
    {
        var engine = new TransformationEngine(RepoRoot());

        TransformResult result = await engine.TransformAsync(BuildTask());

        int maxCc = CyclomaticComplexity.MaxMethodComplexity(
            result.Files.Select(f => (f.Path, f.Content)));

        Assert.True(maxCc > 0, "should have measured at least one method.");
        Assert.True(maxCc < TransformationEngine.ModernMaxMethodCcTarget,
            $"modern max method CC was {maxCc}, expected < {TransformationEngine.ModernMaxMethodCcTarget}.");
    }

    [Fact]
    public void Notes_report_complexity_reduction_from_legacy_49()
    {
        var engine = new TransformationEngine(RepoRoot());

        TransformResult result = engine.EmitFromDisk(BuildTask());

        Assert.Contains($"max-method-cc-before={TransformationEngine.LegacyMaxMethodCc}", result.Notes);
        Assert.Contains("complexity-reduction-pass=true", result.Notes);
        Assert.Contains(result.Notes, n => n.StartsWith("files-emitted=", StringComparison.Ordinal));
    }

    [Fact]
    public void Transcript_fixture_round_trips()
    {
        string transcriptsDir = Path.Combine(RepoRoot(), "fixtures", "transcripts");
        string transcriptPath = Path.Combine(transcriptsDir, "mission-modernization.json");
        Assert.True(File.Exists(transcriptPath), "transcript fixture must exist: " + transcriptPath);

        string json = File.ReadAllText(transcriptPath);
        TransformResult? loaded = JsonSerializer.Deserialize<TransformResult>(json, JsonOpts);
        Assert.NotNull(loaded);
        Assert.NotEmpty(loaded!.Files);
        Assert.True(loaded.CompiledClean);

        // Round-trip: serialize the loaded result again and re-deserialize; Files must be stable.
        string reserialized = JsonSerializer.Serialize(loaded, JsonOpts);
        TransformResult? again = JsonSerializer.Deserialize<TransformResult>(reserialized, JsonOpts);
        Assert.NotNull(again);
        Assert.Equal(loaded.Files.Count, again!.Files.Count);
        Assert.Equal(loaded.AgentId, again.AgentId);
        Assert.Equal(loaded.Notes, again.Notes);
        for (int i = 0; i < loaded.Files.Count; i++)
        {
            Assert.Equal(loaded.Files[i].Path, again.Files[i].Path);
            Assert.Equal(loaded.Files[i].Content, again.Files[i].Content);
        }
    }

    [Fact]
    public void Index_entry_key_matches_computed_offline_replay_key()
    {
        TransformTask task = BuildTask();
        string expectedKey = TranscriptStore.ComputeKey(task.Unit.Id, task.SourceLanguage, task.TargetStack);

        var store = new TranscriptStore(Path.Combine(RepoRoot(), "fixtures", "transcripts"));
        IReadOnlyList<TranscriptIndexEntry> index = store.ReadIndex();

        Assert.Contains(index, e => string.Equals(e.Key, expectedKey, StringComparison.OrdinalIgnoreCase));

        // And the engine replays that exact transcript for this key.
        TransformResult? replayed = store.TryLoad(expectedKey);
        Assert.NotNull(replayed);
        Assert.NotEmpty(replayed!.Files);
    }

    [Fact]
    public async Task Offline_replay_is_deterministic_for_same_task()
    {
        var engine = new TransformationEngine(RepoRoot());

        TransformResult a = await engine.TransformAsync(BuildTask());
        TransformResult b = await engine.TransformAsync(BuildTask());

        Assert.Equal(a.Files.Count, b.Files.Count);
        Assert.Equal(a.AgentId, b.AgentId);
        Assert.Equal(a.Notes, b.Notes);
        for (int i = 0; i < a.Files.Count; i++)
        {
            Assert.Equal(a.Files[i].Content, b.Files[i].Content);
        }
    }
}
