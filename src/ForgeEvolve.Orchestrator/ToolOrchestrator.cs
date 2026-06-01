using ForgeEvolve.Contracts;

namespace ForgeEvolve.Orchestrator;

/// <summary>
/// The FORGE EVOLVE Tool Orchestrator: routes a <see cref="TransformTask"/> to the best available
/// coding agent in one of three modes.
/// <list type="bullet">
///   <item><b>Offline</b> (default, keyless): deterministic transcript replay. The recorded
///   <see cref="TransformResult"/> for the task's deterministic key is returned verbatim. The
///   Thompson-sampling routing decision is still computed (and recorded in the result Notes) so the
///   routing policy is real and testable — but NO model is called and nothing is fabricated.</item>
///   <item><b>Local</b>: sovereign / air-gapped. Routing is restricted to sovereign agents and the
///   live request is handed to the TypeScript bridge (<c>orchestrator/</c>), which uses
///   <c>@577-industries/model-router</c> with <c>sovereignOnly: true</c> over Ollama. Requires a
///   reachable Ollama runtime; otherwise this throws rather than degrade to fabricated output.</item>
///   <item><b>Cloud</b>: real provider APIs via the same TS bridge. Requires API keys.</item>
/// </list>
/// </summary>
public sealed class ToolOrchestrator : IToolOrchestrator
{
    /// <summary>Environment variable that selects the mode. Default <see cref="OrchestratorMode.Offline"/>.</summary>
    public const string ModeEnvVar = "FORGE_ORCHESTRATOR_MODE";

    /// <summary>Optional environment variable that seeds the routing RNG for reproducible demos.</summary>
    public const string SeedEnvVar = "FORGE_ORCHESTRATOR_SEED";

    private const int DefaultSeed = 0x46524745; // "FRGE" — fixed so the demo routing choice is stable.

    private readonly TranscriptStore _transcripts;
    private readonly CapabilityRegistry _registry;
    private readonly int _seed;
    private readonly ILiveModeBridge _liveBridge;

    public OrchestratorMode Mode { get; }

    /// <summary>
    /// Construct an orchestrator. Every dependency is optional and defaults to the keyless Offline
    /// configuration, so <c>new ToolOrchestrator()</c> is a fully-working reviewer demo path.
    /// </summary>
    public ToolOrchestrator(
        OrchestratorMode? mode = null,
        TranscriptStore? transcripts = null,
        CapabilityRegistry? registry = null,
        int? seed = null,
        ILiveModeBridge? liveBridge = null)
    {
        Mode = mode ?? ResolveModeFromEnvironment();
        _transcripts = transcripts ?? TranscriptStore.Open();
        _registry = registry ?? CapabilityRegistry.Default();
        _seed = seed ?? ResolveSeedFromEnvironment();
        _liveBridge = liveBridge ?? new ProcessLiveModeBridge();
    }

    /// <inheritdoc />
    public Task<TransformResult> DispatchAsync(TransformTask task, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ct.ThrowIfCancellationRequested();

        return Mode switch
        {
            OrchestratorMode.Offline => Task.FromResult(DispatchOffline(task)),
            OrchestratorMode.Local => DispatchLiveAsync(task, sovereignOnly: true, ct),
            OrchestratorMode.Cloud => DispatchLiveAsync(task, sovereignOnly: false, ct),
            _ => throw new NotSupportedException($"Unknown orchestrator mode '{Mode}'."),
        };
    }

    /// <summary>
    /// Compute (deterministically) which agent the task WOULD route to, given the mode's sovereign
    /// constraint. Public so the Transformation workstream and tests can inspect routing without
    /// dispatching. The RNG is derived from the fixed seed XOR the task's transcript key, so the
    /// choice is stable per task yet varies across tasks.
    /// </summary>
    public RoutingScore Route(TransformTask task, bool? sovereignOnly = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        var sov = sovereignOnly ?? (Mode == OrchestratorMode.Local);
        var rng = RngFor(task);
        return _registry.Pick(task.FeatureVector, rng, sov);
    }

    // ── Offline replay ───────────────────────────────────────────────────────

    private TransformResult DispatchOffline(TransformTask task)
    {
        // 1. Real routing decision (recorded for provenance, even though replay is deterministic).
        var routing = Route(task, sovereignOnly: false);

        // 2. Deterministic transcript lookup. A missing key throws — never fabricated.
        var key = TranscriptKey.For(task);
        var recorded = _transcripts.Get(key);

        // 3. Re-stamp the replay envelope so the contract invariants hold regardless of how the
        //    transcript JSON was authored: AgentId="offline-replay", Mode=Offline, PromptSha256 set.
        var promptSha = recorded.PromptSha256 ?? TranscriptKey.Sha256Hex(BuildPrompt(task));

        var notes = new List<string>
        {
            $"offline-replay: transcript key {key}",
            $"routing-would-select: {routing.AgentId} (sampled={routing.SampledScore:F4}, mean={routing.PosteriorMean:F4}, sovereign={routing.Sovereign})",
        };
        if (recorded.AgentId is { Length: > 0 } recordedAgent && recordedAgent != "offline-replay")
            notes.Add($"recorded-by: {recordedAgent}");
        notes.AddRange(recorded.Notes);

        return recorded with
        {
            TaskId = task.TaskId,
            AgentId = "offline-replay",
            Mode = OrchestratorMode.Offline,
            PromptSha256 = promptSha,
            Notes = notes,
        };
    }

    // ── Live modes (Local / Cloud) ─────────────────────────────────────────────

    private async Task<TransformResult> DispatchLiveAsync(TransformTask task, bool sovereignOnly, CancellationToken ct)
    {
        // Routing is restricted to sovereign agents in Local mode (air-gapped). The chosen agent +
        // the model-router strategy are passed to the TS bridge, which actually calls the model.
        var routing = _registry.Pick(task.FeatureVector, RngFor(task), sovereignOnly);

        var request = new LiveModeRequest
        {
            TaskId = task.TaskId,
            UnitId = task.Unit.Id,
            SourceLanguage = task.SourceLanguage.ToString(),
            TargetStack = task.TargetStack,
            SovereignOnly = sovereignOnly,
            SelectedAgentId = routing.AgentId,
            FeatureVector = task.FeatureVector,
            PromptSha256 = TranscriptKey.Sha256Hex(BuildPrompt(task)),
        };

        // The bridge throws a clear "requires API keys / Ollama" error when the live runtime is
        // unavailable; we deliberately do NOT fall back to fabricated output.
        return await _liveBridge.InvokeAsync(Mode, request, ct).ConfigureAwait(false);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The deterministic prompt/context surface the model would see. Hashed for provenance. Kept
    /// stable (no timestamps, ordered rules) so the same task always yields the same PromptSha256.
    /// </summary>
    private static string BuildPrompt(TransformTask task)
    {
        var rules = string.Join("\n", task.Rules
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => $"- [{r.Category}] {r.Id}: {r.Statement}"));
        return
            $"unit={task.Unit.Id}\n" +
            $"service={task.Unit.ProposedServiceName}\n" +
            $"sourceLanguage={task.SourceLanguage}\n" +
            $"targetStack={task.TargetStack}\n" +
            $"clarSha256={TranscriptKey.Sha256Hex(task.ClarDocumentJson)}\n" +
            $"rules:\n{rules}\n";
    }

    private Random RngFor(TransformTask task)
    {
        // Derive a per-task seed so the routing choice is stable for a given task but differs across
        // tasks — combine the fixed base seed with the transcript key hash.
        var keyHash = TranscriptKey.For(task).GetHashCode(StringComparison.Ordinal);
        return new Random(_seed ^ keyHash);
    }

    private static OrchestratorMode ResolveModeFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(ModeEnvVar);
        if (string.IsNullOrWhiteSpace(raw))
            return OrchestratorMode.Offline;

        return Enum.TryParse<OrchestratorMode>(raw.Trim(), ignoreCase: true, out var mode)
            ? mode
            : throw new ArgumentException(
                $"{ModeEnvVar}='{raw}' is not a valid orchestrator mode. Expected one of: " +
                $"{string.Join(", ", Enum.GetNames<OrchestratorMode>())}.");
    }

    private static int ResolveSeedFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(SeedEnvVar);
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultSeed;
        return int.TryParse(raw.Trim(), out var seed) ? seed : DefaultSeed;
    }
}
