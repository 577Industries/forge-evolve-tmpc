using ForgeEvolve.Contracts;
using Xunit;

namespace ForgeEvolve.Orchestrator.Tests;

public class TranscriptKeyTests
{
    [Fact]
    public void Key_Is_Sha256_Of_Canonical_Triple()
    {
        // Independently-computable: sha256("sample-dummy-unit|CSharp|dotnet8").
        var key = TranscriptKey.For(TaskFixtures.SampleDummyTask());
        Assert.Equal("d5d32ade151a37170d1b4d712cb5392dcb903c8eb14dda3bcc157695ba6b5790", key);
    }

    [Fact]
    public void Key_Is_Deterministic_And_Discriminating()
    {
        var a = TranscriptKey.For(TaskFixtures.SampleDummyTask());
        var aAgain = TranscriptKey.For(TaskFixtures.SampleDummyTask());
        var b = TranscriptKey.For(TaskFixtures.UnknownTask());

        Assert.Equal(a, aAgain);
        Assert.NotEqual(a, b);
    }
}

public class OfflineDispatchTests
{
    private static ToolOrchestrator Offline() => new(mode: OrchestratorMode.Offline);

    [Fact]
    public void Mode_Defaults_To_Offline_With_No_Configuration()
    {
        // Construct with nothing and no FORGE_ORCHESTRATOR_MODE set in this test process.
        var prior = Environment.GetEnvironmentVariable(ToolOrchestrator.ModeEnvVar);
        Environment.SetEnvironmentVariable(ToolOrchestrator.ModeEnvVar, null);
        try
        {
            var orchestrator = new ToolOrchestrator();
            Assert.Equal(OrchestratorMode.Offline, orchestrator.Mode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ToolOrchestrator.ModeEnvVar, prior);
        }
    }

    [Fact]
    public async Task Offline_Returns_Recorded_Transcript_For_Known_Key()
    {
        var orchestrator = Offline();
        var result = await orchestrator.DispatchAsync(TaskFixtures.SampleDummyTask());

        // The recorded transcript's payload is replayed verbatim ...
        var file = Assert.Single(result.Files);
        Assert.Equal("SampleUnit/SampleService.cs", file.Path);
        Assert.Contains("checked(a + b)", file.Content);
        Assert.True(result.CompiledClean);
        Assert.Equal(0.91, result.QualityEstimate, 3);

        // ... but the replay envelope is re-stamped per the contract.
        Assert.Equal("offline-replay", result.AgentId);
        Assert.Equal(OrchestratorMode.Offline, result.Mode);
        Assert.False(string.IsNullOrWhiteSpace(result.PromptSha256));
        Assert.Equal("sample-dummy-task", result.TaskId);
    }

    [Fact]
    public async Task Offline_Records_The_Routing_Decision_In_Notes()
    {
        var orchestrator = Offline();
        var result = await orchestrator.DispatchAsync(TaskFixtures.SampleDummyTask());

        Assert.Contains(result.Notes, n => n.StartsWith("offline-replay: transcript key", StringComparison.Ordinal));
        Assert.Contains(result.Notes, n => n.StartsWith("routing-would-select:", StringComparison.Ordinal));
        // The original recorded notes are preserved (provenance, not discarded).
        Assert.Contains(result.Notes, n => n.Contains("dummy migration unit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Offline_Replay_Is_Deterministic_Across_Runs()
    {
        var first = await Offline().DispatchAsync(TaskFixtures.SampleDummyTask());
        var second = await Offline().DispatchAsync(TaskFixtures.SampleDummyTask());

        Assert.Equal(first.PromptSha256, second.PromptSha256);
        Assert.Equal(first.Files[0].Content, second.Files[0].Content);
        Assert.Equal(first.Notes, second.Notes); // identical, including the routing note
    }

    [Fact]
    public async Task Offline_Throws_Clear_Error_For_Unknown_Key_And_Never_Fabricates()
    {
        var orchestrator = Offline();
        var ex = await Assert.ThrowsAsync<TranscriptNotFoundException>(
            () => orchestrator.DispatchAsync(TaskFixtures.UnknownTask()));

        Assert.Contains("No recorded transcript", ex.Message);
        Assert.Contains("will not fabricate", ex.Message);
        // The unknown key must appear so a developer can wire up the missing transcript.
        Assert.Contains(TranscriptKey.For(TaskFixtures.UnknownTask()), ex.Message);
    }
}

public class RoutingRegistryTests
{
    [Fact]
    public void Default_Registry_Has_Sovereign_And_NonSovereign_Agents()
    {
        var registry = CapabilityRegistry.Default();
        Assert.Contains(registry.Agents, a => a.Sovereign);
        Assert.Contains(registry.Agents, a => !a.Sovereign);
        // claude-code carries the strongest prior (highest posterior mean).
        var top = registry.Agents.OrderByDescending(a => a.PosteriorMean).First();
        Assert.Equal("claude-code", top.AgentId);
    }

    [Fact]
    public void Routing_Deterministically_Prefers_Higher_Capability_Agent_For_A_Feature_Vector()
    {
        var registry = CapabilityRegistry.Default();
        // A complexity-heavy, large-context task: claude-code dominates on both posterior and weights.
        var features = new Dictionary<string, double> { ["complexity"] = 1.0, ["contextSize"] = 1.0 };

        // Average many seeded Thompson draws; the higher-capability agent must win the plurality,
        // and a fixed seed must give a byte-identical pick (determinism).
        var winners = new Dictionary<string, int>();
        for (var seed = 0; seed < 200; seed++)
        {
            var pick = registry.Pick(features, new Random(seed));
            winners[pick.AgentId] = winners.GetValueOrDefault(pick.AgentId) + 1;
        }
        var plurality = winners.OrderByDescending(kv => kv.Value).First().Key;
        Assert.Equal("claude-code", plurality);

        // Determinism: same seed → same pick.
        var a = registry.Pick(features, new Random(12345));
        var b = registry.Pick(features, new Random(12345));
        Assert.Equal(a.AgentId, b.AgentId);
        Assert.Equal(a.SampledScore, b.SampledScore, 12);
    }

    [Fact]
    public void SovereignOnly_Routing_Never_Selects_A_NonSovereign_Agent()
    {
        var registry = CapabilityRegistry.Default();
        var features = new Dictionary<string, double> { ["complexity"] = 1.0 };

        for (var seed = 0; seed < 200; seed++)
        {
            var pick = registry.Pick(features, new Random(seed), sovereignOnly: true);
            Assert.True(pick.Sovereign, $"seed {seed} picked non-sovereign agent {pick.AgentId} under sovereignOnly");
        }
    }

    [Fact]
    public void Orchestrator_Route_Is_Stable_Per_Task()
    {
        var orchestrator = new ToolOrchestrator(mode: OrchestratorMode.Offline);
        var task = TaskFixtures.SampleDummyTask(new Dictionary<string, double> { ["complexity"] = 1.0 });

        var a = orchestrator.Route(task);
        var b = orchestrator.Route(task);
        Assert.Equal(a.AgentId, b.AgentId);
        Assert.Equal(a.SampledScore, b.SampledScore, 12);
    }

    [Fact]
    public void Local_Mode_Route_Restricts_To_Sovereign_Agents()
    {
        var orchestrator = new ToolOrchestrator(mode: OrchestratorMode.Local);
        var task = TaskFixtures.SampleDummyTask(new Dictionary<string, double> { ["complexity"] = 1.0 });
        var pick = orchestrator.Route(task); // sovereignOnly defaults to true in Local mode
        Assert.True(pick.Sovereign);
    }
}

public class BetaSamplerTests
{
    [Fact]
    public void Samples_Are_In_Unit_Interval()
    {
        var rng = new Random(7);
        for (var i = 0; i < 1000; i++)
        {
            var s = BetaSampler.Sample(2.0, 5.0, rng);
            Assert.InRange(s, 0.0, 1.0);
        }
    }

    [Fact]
    public void Sample_Mean_Approximates_Alpha_Over_AlphaPlusBeta()
    {
        // E[Beta(a,b)] = a/(a+b). With a=8, b=2 the mean is 0.8.
        var rng = new Random(99);
        double sum = 0;
        const int n = 20000;
        for (var i = 0; i < n; i++) sum += BetaSampler.Sample(8.0, 2.0, rng);
        var mean = sum / n;
        Assert.InRange(mean, 0.78, 0.82);
    }

    [Fact]
    public void Same_Seed_Produces_Identical_Sequence()
    {
        var a = new Random(2024);
        var b = new Random(2024);
        for (var i = 0; i < 100; i++)
            Assert.Equal(BetaSampler.Sample(3.0, 4.0, a), BetaSampler.Sample(3.0, 4.0, b), 12);
    }
}

public class LiveModeTests
{
    [Fact]
    public async Task Local_Mode_Without_Bridge_Throws_Requires_Ollama_Error()
    {
        // No FORGE_ORCHESTRATOR_BRIDGE set → ProcessLiveModeBridge must surface a clear error,
        // never a fabricated TransformResult.
        var orchestrator = new ToolOrchestrator(mode: OrchestratorMode.Local);
        var ex = await Assert.ThrowsAsync<LiveModeUnavailableException>(
            () => orchestrator.DispatchAsync(TaskFixtures.SampleDummyTask()));
        Assert.Contains("Ollama", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cloud_Mode_Without_Bridge_Throws_Requires_Keys_Error()
    {
        var orchestrator = new ToolOrchestrator(mode: OrchestratorMode.Cloud);
        var ex = await Assert.ThrowsAsync<LiveModeUnavailableException>(
            () => orchestrator.DispatchAsync(TaskFixtures.SampleDummyTask()));
        Assert.Contains("API key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Live_Bridge_Result_Is_Passed_Through_In_Cloud_Mode()
    {
        // Inject a fake bridge to prove the live path returns the bridge's result unchanged
        // (the model-router selection happens inside the TS bridge; here we assert the seam).
        var fake = new FakeBridge();
        var orchestrator = new ToolOrchestrator(mode: OrchestratorMode.Cloud, liveBridge: fake);

        var result = await orchestrator.DispatchAsync(TaskFixtures.SampleDummyTask());
        Assert.Equal("fake-live-agent", result.AgentId);
        Assert.Equal(OrchestratorMode.Cloud, fake.LastMode);
        Assert.False(fake.LastRequest!.SovereignOnly); // Cloud → not sovereign-restricted
    }

    [Fact]
    public async Task Live_Bridge_Receives_SovereignOnly_In_Local_Mode()
    {
        var fake = new FakeBridge();
        var orchestrator = new ToolOrchestrator(mode: OrchestratorMode.Local, liveBridge: fake);
        await orchestrator.DispatchAsync(TaskFixtures.SampleDummyTask(
            new Dictionary<string, double> { ["complexity"] = 1.0 }));

        Assert.Equal(OrchestratorMode.Local, fake.LastMode);
        Assert.True(fake.LastRequest!.SovereignOnly);
        // The agent handed to the bridge must be one of the sovereign agents.
        Assert.Contains(fake.LastRequest!.SelectedAgentId, new[] { "ollama-codellama-70b", "ollama-mistral-small" });
    }

    private sealed class FakeBridge : ILiveModeBridge
    {
        public OrchestratorMode LastMode { get; private set; }
        public LiveModeRequest? LastRequest { get; private set; }

        public Task<TransformResult> InvokeAsync(OrchestratorMode mode, LiveModeRequest request, CancellationToken ct = default)
        {
            LastMode = mode;
            LastRequest = request;
            return Task.FromResult(new TransformResult
            {
                TaskId = request.TaskId,
                Files = Array.Empty<EmittedFile>(),
                AgentId = "fake-live-agent",
                Mode = mode,
                PromptSha256 = request.PromptSha256,
            });
        }
    }
}
