using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Orchestrator;

/// <summary>
/// The request the C# orchestrator hands to the TypeScript live-mode bridge (<c>orchestrator/</c>).
/// Carries the routing decision and the deterministic prompt hash; the bridge resolves the concrete
/// model via <c>@577-industries/model-router</c> and performs the actual model call.
/// </summary>
public sealed record LiveModeRequest
{
    public required string TaskId { get; init; }
    public required string UnitId { get; init; }
    public required string SourceLanguage { get; init; }
    public required string TargetStack { get; init; }
    /// <summary>True in Local (air-gapped) mode — the router must select a sovereign/open-source model.</summary>
    public required bool SovereignOnly { get; init; }
    /// <summary>The agent the C# Thompson-sampling registry chose (the router reconciles to a model).</summary>
    public required string SelectedAgentId { get; init; }
    public IReadOnlyDictionary<string, double> FeatureVector { get; init; } = new Dictionary<string, double>();
    public required string PromptSha256 { get; init; }
}

/// <summary>
/// Seam to the live-mode runtime. In Offline mode this is never called. In Local/Cloud mode the
/// default implementation shells out to the TypeScript bridge; tests inject a fake.
/// </summary>
public interface ILiveModeBridge
{
    Task<TransformResult> InvokeAsync(OrchestratorMode mode, LiveModeRequest request, CancellationToken ct = default);
}

/// <summary>
/// Default live-mode bridge: invokes the Node TypeScript orchestrator (<c>orchestrator/</c>) as a
/// child process and parses its emitted <see cref="TransformResult"/> JSON from stdout.
/// </summary>
/// <remarks>
/// <para>
/// This is the documented integration point between the C# engine and the already-built 577 router.
/// The TS entrypoint (<c>orchestrator/src/route.ts</c> compiled to <c>dist/route.js</c>, run via the
/// package's <c>route</c> bin / <c>node</c>) receives the <see cref="LiveModeRequest"/> as JSON on
/// stdin, calls <c>createRouter().route({ sovereignOnly })</c>, performs the model call (Ollama for
/// Local, provider API for Cloud), and prints a <see cref="TransformResult"/> as JSON.
/// </para>
/// <para>
/// When the bridge is not present or the live runtime (Ollama / API keys) is unavailable, this
/// throws <see cref="LiveModeUnavailableException"/>. It NEVER fabricates a transform result — the
/// keyless reviewer demo uses Offline replay exclusively.
/// </para>
/// </remarks>
public sealed class ProcessLiveModeBridge : ILiveModeBridge
{
    /// <summary>Env var pointing at the TS bridge entrypoint (compiled dist/route.js or a wrapper).</summary>
    public const string BridgeEntrypointEnvVar = "FORGE_ORCHESTRATOR_BRIDGE";

    /// <summary>Env var for the Node executable (defaults to "node" on PATH).</summary>
    public const string NodeExecutableEnvVar = "FORGE_ORCHESTRATOR_NODE";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TransformResult> InvokeAsync(OrchestratorMode mode, LiveModeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entrypoint = Environment.GetEnvironmentVariable(BridgeEntrypointEnvVar);
        if (string.IsNullOrWhiteSpace(entrypoint) || !File.Exists(entrypoint))
        {
            throw new LiveModeUnavailableException(
                $"{mode} mode requires the TypeScript live-mode bridge (orchestrator/), but it was not found. " +
                $"Set {BridgeEntrypointEnvVar} to the built bridge entrypoint (orchestrator/dist/route.js) and ensure " +
                (mode == OrchestratorMode.Local
                    ? "a local Ollama runtime is reachable for sovereign/air-gapped routing. "
                    : "the relevant provider API keys are present. ") +
                "The keyless reviewer demo uses Offline transcript replay instead — it never calls a model.");
        }

        var node = Environment.GetEnvironmentVariable(NodeExecutableEnvVar);
        node = string.IsNullOrWhiteSpace(node) ? "node" : node;

        var payload = JsonSerializer.Serialize(new
        {
            mode = mode.ToString().ToLowerInvariant(),
            request,
        }, JsonOptions);

        var psi = new ProcessStartInfo
        {
            FileName = node,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(entrypoint);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new LiveModeUnavailableException(
                $"Failed to launch the live-mode bridge via '{node}'. Is Node.js installed? " +
                "The offline reviewer demo does not require Node.", ex);
        }

        await process.StandardInput.WriteAsync(payload.AsMemory(), ct).ConfigureAwait(false);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new LiveModeUnavailableException(
                $"{mode} live-mode bridge exited with code {process.ExitCode}. " +
                $"This typically means the live runtime (Ollama for Local, API keys for Cloud) is unavailable. " +
                $"Bridge stderr: {Truncate(stderr, 2000)}");
        }

        var result = JsonSerializer.Deserialize<TransformResult>(stdout, JsonOptions);
        if (result is null)
        {
            throw new LiveModeUnavailableException(
                $"{mode} live-mode bridge produced no parseable TransformResult on stdout. " +
                $"stdout: {Truncate(stdout, 1000)}");
        }
        return result;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "<empty>" : (s.Length <= max ? s : s[..max] + "…");
}

/// <summary>
/// Thrown when Local/Cloud live-mode is requested but its runtime (Ollama or provider API keys, or
/// the TS bridge itself) is unavailable. The orchestrator surfaces this rather than fabricating output.
/// </summary>
public sealed class LiveModeUnavailableException : Exception
{
    public LiveModeUnavailableException(string message) : base(message) { }
    public LiveModeUnavailableException(string message, Exception inner) : base(message, inner) { }
}
