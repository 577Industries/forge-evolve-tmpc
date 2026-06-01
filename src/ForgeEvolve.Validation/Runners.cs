// FORGE EVOLVE for TMPC — the two equivalence runners (the legacy/modern adapters).
//
// The validator is *implementation-agnostic*: it only ever sees an ILegacyRunner and an
// IModernRunner, each a `string -> string` (JSON in, JSON out) function. That is the whole
// integration seam. At integration (P3) the real modern component is injected as the
// IModernRunner delegate; here, the engine is proven correct on the surrogate legacy and on
// modern stand-ins derived from the frozen corpus (legacyOutput-as-modern, referenceOutput-
// as-modern, and a deliberately-perturbed modern).

using Tmpc.Surrogate.Legacy;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Validation;

/// <summary>
/// Wraps the synthetic, intentionally-legacy <see cref="MissionProcessor.ProcessMission"/>.
/// This is the real legacy behavior under test (defects D1/D2/D3 included). It is byte-
/// faithful to the corpus <c>legacyOutput</c> answer key (proven by tools/LegacyCheck).
/// </summary>
public sealed class LegacyRunner : ILegacyRunner
{
    /// <inheritdoc/>
    public string Run(string inputJson) => MissionProcessor.ProcessMission(inputJson);
}

/// <summary>
/// The modern adapter. Constructed with a <see cref="Func{String, String}"/> so the engine
/// is decoupled from any particular modern implementation:
/// <list type="bullet">
///   <item>at integration (P3) this delegate is the REAL modernized component;</item>
///   <item>in the engine's own tests it is a corpus-derived stand-in — e.g. echo the corpus
///   <c>legacyOutput</c> (perfect-equivalence case), echo the corpus <c>referenceOutput</c>
///   (the bug-fixed modern, which intentionally diverges on the 321 known-divergent vectors),
///   or a perturbed-modern that breaks a continuous field beyond tolerance.</item>
/// </list>
/// </summary>
public sealed class ModernRunner : IModernRunner
{
    private readonly Func<string, string> _modern;

    /// <summary>Build a modern runner from a JSON-in/JSON-out delegate.</summary>
    /// <param name="modern">The modern implementation (or a test stand-in).</param>
    public ModernRunner(Func<string, string> modern)
        => _modern = modern ?? throw new ArgumentNullException(nameof(modern));

    /// <inheritdoc/>
    public string Run(string inputJson) => _modern(inputJson);
}
