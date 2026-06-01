using ForgeEvolve.Contracts;

namespace ForgeEvolve.Orchestrator;

/// <summary>
/// A coding agent the orchestrator can route a transform task to, together with the
/// Beta posterior over its success probability and the capability tags it advertises.
/// </summary>
/// <remarks>
/// <para>
/// <b>Alpha</b> and <b>Beta</b> are the parameters of a Beta(α, β) distribution — the
/// conjugate prior for a Bernoulli "did this agent produce a clean, equivalent transform?"
/// signal. α ≈ 1 + observed successes, β ≈ 1 + observed failures. A higher α/(α+β) mean and
/// a tighter posterior make the agent more likely to be sampled.
/// </para>
/// <para>
/// The registry is seeded from prior FORGE EVOLVE telemetry (these priors are illustrative
/// for the keyless surrogate demo, not measured TMPC numbers). In a live deployment the
/// posteriors are updated from validator outcomes after each dispatch.
/// </para>
/// </remarks>
public sealed record AgentCapability
{
    /// <summary>Stable agent id, e.g. "claude-code", "ollama-codellama", "gpt-4o".</summary>
    public required string AgentId { get; init; }

    /// <summary>Whether the agent runs fully sovereign (local weights, no egress) — eligible in Local mode.</summary>
    public required bool Sovereign { get; init; }

    /// <summary>Beta posterior α (≈ 1 + successes). Must be &gt; 0.</summary>
    public required double Alpha { get; init; }

    /// <summary>Beta posterior β (≈ 1 + failures). Must be &gt; 0.</summary>
    public required double Beta { get; init; }

    /// <summary>
    /// Per-capability competence weights in [0,1] keyed by feature-vector dimension
    /// (e.g. "complexity", "crypto", "contextSize"). Used to bias the posterior mean by how
    /// well the agent matches a task's feature vector, so routing is feature-aware, not blind.
    /// </summary>
    public IReadOnlyDictionary<string, double> CapabilityWeights { get; init; } =
        new Dictionary<string, double>();

    /// <summary>Posterior mean E[θ] = α / (α + β).</summary>
    public double PosteriorMean => Alpha / (Alpha + Beta);
}

/// <summary>One agent's score for a particular task, produced by <see cref="CapabilityRegistry"/>.</summary>
public sealed record RoutingScore
{
    public required string AgentId { get; init; }
    /// <summary>The value drawn from the agent's Beta posterior (Thompson sample), feature-weighted.</summary>
    public required double SampledScore { get; init; }
    /// <summary>The deterministic posterior mean (no sampling) — used for explainability / tie audit.</summary>
    public required double PosteriorMean { get; init; }
    public required bool Sovereign { get; init; }
}

/// <summary>
/// A Thompson-sampling capability registry: it picks WHICH agent a transform task would be
/// routed to by drawing one sample from each candidate agent's Beta posterior (scaled by how
/// well the agent's capability weights match the task feature vector) and selecting the max.
/// </summary>
/// <remarks>
/// In <see cref="OrchestratorMode.Offline"/> the dispatch still returns the recorded transcript,
/// but the routing decision computed here is real and recorded in the result Notes — so the
/// routing policy is exercised and testable without any model call. The RNG is seedable so the
/// reviewer demo (and the unit tests) get a deterministic, reproducible choice.
/// </remarks>
public sealed class CapabilityRegistry
{
    private readonly IReadOnlyList<AgentCapability> _agents;

    public CapabilityRegistry(IReadOnlyList<AgentCapability> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        if (agents.Count == 0)
            throw new ArgumentException("Capability registry must contain at least one agent.", nameof(agents));
        foreach (var a in agents)
        {
            if (a.Alpha <= 0 || a.Beta <= 0)
                throw new ArgumentException($"Agent '{a.AgentId}' has non-positive Beta parameters.", nameof(agents));
        }
        _agents = agents;
    }

    /// <summary>The agents this registry can route to (read-only).</summary>
    public IReadOnlyList<AgentCapability> Agents => _agents;

    /// <summary>
    /// The default FORGE EVOLVE agent roster. "claude-code" is the strongest general transformer;
    /// the Ollama/Llama agents are sovereign (local) options for air-gapped Local mode.
    /// Priors are illustrative defaults for the surrogate demo, NOT measured TMPC metrics.
    /// </summary>
    public static CapabilityRegistry Default() => new(new[]
    {
        new AgentCapability
        {
            AgentId = "claude-code",
            Sovereign = false,
            Alpha = 47, Beta = 5, // strong prior: ~90% historical clean-transform rate
            CapabilityWeights = new Dictionary<string, double>
            {
                ["complexity"] = 0.95, ["crypto"] = 0.90, ["contextSize"] = 0.95, ["routing"] = 0.92,
            },
        },
        new AgentCapability
        {
            AgentId = "gpt-4o",
            Sovereign = false,
            Alpha = 34, Beta = 8,
            CapabilityWeights = new Dictionary<string, double>
            {
                ["complexity"] = 0.85, ["crypto"] = 0.80, ["contextSize"] = 0.80, ["routing"] = 0.84,
            },
        },
        new AgentCapability
        {
            // Sovereign / air-gapped option (Ollama-hosted). Eligible in Local mode.
            AgentId = "ollama-codellama-70b",
            Sovereign = true,
            Alpha = 22, Beta = 14,
            CapabilityWeights = new Dictionary<string, double>
            {
                ["complexity"] = 0.70, ["crypto"] = 0.62, ["contextSize"] = 0.55, ["routing"] = 0.68,
            },
        },
        new AgentCapability
        {
            AgentId = "ollama-mistral-small",
            Sovereign = true,
            Alpha = 14, Beta = 18,
            CapabilityWeights = new Dictionary<string, double>
            {
                ["complexity"] = 0.55, ["crypto"] = 0.45, ["contextSize"] = 0.40, ["routing"] = 0.52,
            },
        },
    });

    /// <summary>
    /// Score every candidate agent for a task by Thompson sampling. If
    /// <paramref name="sovereignOnly"/> is set (Local / air-gapped mode), non-sovereign agents
    /// are filtered out first. Results are returned highest-sample-first.
    /// </summary>
    public IReadOnlyList<RoutingScore> Score(
        IReadOnlyDictionary<string, double> featureVector,
        Random rng,
        bool sovereignOnly = false)
    {
        ArgumentNullException.ThrowIfNull(featureVector);
        ArgumentNullException.ThrowIfNull(rng);

        var candidates = sovereignOnly ? _agents.Where(a => a.Sovereign).ToList() : _agents.ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException(
                "No sovereign agents available for air-gapped routing. Check the capability registry.");

        var scores = new List<RoutingScore>(candidates.Count);
        foreach (var agent in candidates)
        {
            // Thompson sample θ ~ Beta(α, β), then weight by how well the agent's capability
            // weights cover the task's feature vector. The feature match is the dot product of
            // (matched capability weight × feature magnitude) normalized by total feature mass —
            // a task that is all "complexity" rewards an agent strong at complexity.
            var theta = BetaSampler.Sample(agent.Alpha, agent.Beta, rng);
            var match = FeatureMatch(agent.CapabilityWeights, featureVector);
            var sampled = theta * match;

            scores.Add(new RoutingScore
            {
                AgentId = agent.AgentId,
                SampledScore = sampled,
                PosteriorMean = agent.PosteriorMean * match,
                Sovereign = agent.Sovereign,
            });
        }

        // Highest sampled score wins; ties (and degenerate all-zero feature vectors) break by
        // posterior mean then by agent id, so the choice is fully deterministic given the RNG.
        return scores
            .OrderByDescending(s => s.SampledScore)
            .ThenByDescending(s => s.PosteriorMean)
            .ThenBy(s => s.AgentId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Pick the single agent a task WOULD be routed to. Uses the seeded RNG for reproducibility.
    /// </summary>
    public RoutingScore Pick(
        IReadOnlyDictionary<string, double> featureVector,
        Random rng,
        bool sovereignOnly = false)
        => Score(featureVector, rng, sovereignOnly)[0];

    /// <summary>
    /// Coverage of a task's feature vector by an agent's capability weights, in (0,1].
    /// With an empty feature vector every agent matches equally (1.0) so routing reduces to the
    /// pure posterior — the higher-capability agent still wins, which keeps the policy testable.
    /// </summary>
    private static double FeatureMatch(
        IReadOnlyDictionary<string, double> weights,
        IReadOnlyDictionary<string, double> featureVector)
    {
        double total = 0, matched = 0;
        foreach (var (dim, magnitude) in featureVector)
        {
            var mag = Math.Abs(magnitude);
            total += mag;
            if (weights.TryGetValue(dim, out var w))
                matched += mag * w;
        }
        if (total <= 0) return 1.0; // no features → no discrimination from feature match
        return matched / total;
    }
}
