// FORGE EVOLVE for TMPC — frozen module interfaces (the integration seams).
//
// Each pipeline stage implements exactly one of these. Stages depend ONLY on this contract
// assembly, never on each other's implementations — that is what lets the eight workstreams be
// built in parallel git worktrees and integrated in dependency order.
//
// Pipeline:  Discovery -> CLAR -> Planner -> Orchestrator/Transformer -> Validator -> CyberOverlay
//            with Governance recording every step.

namespace ForgeEvolve.Contracts;

/// <summary>Stage 1 — parse, build the dependency graph, extract rules, inventory crypto.</summary>
public interface IDiscoveryEngine
{
    DiscoveryReport Analyze(IReadOnlyList<SourceArtifact> sources);
}

/// <summary>Cross-Language Abstract Representation provider: lift source modules into CLAR.</summary>
public interface IClarProvider
{
    /// <summary>Lift a module (with its discovery context) into a CLAR JSON-LD document.</summary>
    string Lift(ModuleNode module, DiscoveryReport context);

    /// <summary>Validate a CLAR document against the published schema. Returns errors (empty = valid).</summary>
    IReadOnlyList<string> Validate(string clarDocumentJson);
}

/// <summary>Stage 2 — risk-score modules and decompose into ordered migration units.</summary>
public interface IMigrationPlanner
{
    MigrationPlan Plan(DiscoveryReport discovery);
}

/// <summary>
/// Routes a transform task to the best available coding agent. Runs in Offline (deterministic
/// transcript replay — default, keyless), Local (sovereign/air-gapped), or Cloud mode.
/// </summary>
public interface IToolOrchestrator
{
    OrchestratorMode Mode { get; }
    Task<TransformResult> DispatchAsync(TransformTask task, CancellationToken ct = default);
}

/// <summary>Stage 3 — produce modernized source for a migration unit (uses the orchestrator).</summary>
public interface ITransformer
{
    Task<TransformResult> TransformAsync(TransformTask task, CancellationToken ct = default);
}

/// <summary>Stage 4 — prove behavioral equivalence between legacy and modern implementations.</summary>
public interface IEquivalenceValidator
{
    EquivalenceReport Verify(
        string unitId,
        ILegacyRunner legacy,
        IModernRunner modern,
        IReadOnlyList<EquivalenceTestVector> vectors,
        ToleranceConfig tolerance);
}

/// <summary>Executes the legacy implementation on a JSON input, returning a JSON output.</summary>
public interface ILegacyRunner
{
    string Run(string inputJson);
}

/// <summary>Executes the modernized implementation on a JSON input, returning a JSON output.</summary>
public interface IModernRunner
{
    string Run(string inputJson);
}

/// <summary>Stage 5 — security analysis + cATO artifact generation (STIG, 800-53, SBOM, POA&amp;M, provenance).</summary>
public interface ICyberOverlay
{
    CatoArtifacts Generate(
        IReadOnlyList<SourceArtifact> legacy,
        IReadOnlyList<EmittedFile> modern,
        DiscoveryReport discovery,
        string outputDir);
}

/// <summary>Records tamper-evident provenance and evaluates human-in-the-loop review gates.</summary>
public interface IGovernance
{
    ProvenanceRecord Record(string action, string actor, string payloadJson);
    string CurrentMerkleRoot();
    ReviewGate Evaluate(string gateId, IReadOnlyDictionary<string, string> evidence);
}
