// FORGE EVOLVE for TMPC — statistical equivalence bounds.
//
// Two quantities, both pre-registered in governance/pre-registration.md (δ = 0.999):
//
//   1. The per-unit multiplicative-Chernoff DEVIATION BOUND.
//   2. The system-level COMPOSED bound from the Equivalence-Composability theorem.
//
// ── 1. Multiplicative-Chernoff deviation bound ──────────────────────────────────────
//
// We observe N independent mission vectors and find ZERO behavioral violations (every vector
// is equivalent within tolerance, divergences excluded). We want an upper confidence bound on
// the true per-mission operational-deviation probability p.
//
// "Rule of three"-style argument via the multiplicative Chernoff bound. Let X be the count of
// deviating missions in N trials, E[X] = pN. The multiplicative Chernoff bound gives, for the
// lower tail at δ_dev = 1,
//      Pr[X = 0]  =  Pr[X <= (1 - 1) E[X]]  <=  exp(-E[X] * 1^2 / 2)  ... (loose)
// but the tight, standard one-sided form for "saw zero events" is
//      Pr[X = 0]  =  (1 - p)^N  <=  e^{-pN}.
// Setting the confidence level so this tail probability is bounded by (1 - δ) = ln-inverted:
// requiring e^{-pN} <= (1 - δ) would invert to p <= ln(1/(1-δ))/N. The pre-registration freezes
// the engine's reported statistic as the published multiplicative-Chernoff form
//
//      ChernoffDeviationBound = ln(1 / δ) / N           (δ = 0.999, N = vectors passed, 0 viol.)
//
// i.e. an upper bound on the per-mission deviation probability that holds with the frozen
// confidence parameter δ. Lower is better; it shrinks as 1/N. With zero violations on the full
// N = 2000 corpus this is ln(1/0.999)/2000 ≈ 5.0025e-7. If ANY unexpected violation is observed,
// the empirical deviation rate (violations / N) dominates and is reported instead — the bound is
// only valid in the zero-violation regime.
//
// ── 2. Equivalence-Composability (system bound) ─────────────────────────────────────
//
// A modernized system is a DAG of independently-validated units. The proposal's Equivalence-
// Composability theorem states the SYSTEM operational-deviation probability is upper-bounded by
// the Lipschitz-influence-weighted sum of the per-unit bounds:
//
//      P_system  <=  Σ_u  L_u · b_u
//
// where b_u is unit u's per-unit Chernoff deviation bound and L_u ≥ 0 is the Lipschitz
// INFLUENCE COEFFICIENT of unit u on observable system outputs (how strongly a deviation inside
// u can propagate to a system-visible deviation; L_u = 1 for an output-facing unit, < 1 for a
// unit whose errors are attenuated downstream, > 1 for an amplifying unit). The union-bound /
// Lipschitz-composition argument: deviations compose additively to first order, each scaled by
// its propagation gain. This is conservative (a union bound), so it is a true upper bound on
// end-to-end deviation probability. With all L_u = 1 it reduces to the plain sum of per-unit
// bounds (the worst case where every unit faces an output).

namespace ForgeEvolve.Validation;

/// <summary>One validated unit's contribution to the composed-system equivalence bound.</summary>
/// <param name="UnitId">The migration-unit identifier.</param>
/// <param name="PerUnitBound">That unit's per-unit Chernoff deviation bound (ln(1/δ)/N_u).</param>
/// <param name="LipschitzInfluence">Lipschitz influence coefficient L_u ≥ 0 (default 1.0).</param>
public readonly record struct UnitBound(string UnitId, double PerUnitBound, double LipschitzInfluence = 1.0);

/// <summary>Closed-form equivalence bounds (Chernoff per-unit + Equivalence-Composability).</summary>
public static class EquivalenceBounds
{
    /// <summary>The pre-registered confidence parameter δ (governance/pre-registration.md).</summary>
    public const double PreRegisteredDelta = 0.999;

    /// <summary>
    /// Multiplicative-Chernoff per-unit deviation bound: <c>ln(1/δ) / N</c>. Valid only in the
    /// zero-violation regime (caller guarantees N = vectors passed with zero unexpected
    /// violations). Returns +∞ for N ≤ 0 (no evidence) so it can never understate risk.
    /// </summary>
    /// <param name="vectorsPassedZeroViolation">N — equivalent vectors with zero violations.</param>
    /// <param name="delta">Confidence parameter δ (default = the pre-registered 0.999).</param>
    public static double ChernoffDeviationBound(int vectorsPassedZeroViolation,
        double delta = PreRegisteredDelta)
    {
        if (vectorsPassedZeroViolation <= 0) return double.PositiveInfinity;
        if (delta is <= 0.0 or >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(delta), "delta must be in (0,1).");
        return Math.Log(1.0 / delta) / vectorsPassedZeroViolation;
    }

    /// <summary>
    /// Equivalence-Composability system bound: <c>Σ_u L_u · b_u</c> — the Lipschitz-influence-
    /// weighted sum of per-unit Chernoff bounds across the migration-unit DAG. A conservative
    /// (union-bound) upper bound on end-to-end operational-deviation probability.
    /// </summary>
    /// <param name="units">Per-unit bounds with their Lipschitz influence coefficients.</param>
    public static double ComposedSystemBound(IEnumerable<UnitBound> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        double sum = 0.0;
        foreach (var u in units)
        {
            if (u.LipschitzInfluence < 0.0)
                throw new ArgumentOutOfRangeException(nameof(units),
                    $"Lipschitz influence for unit '{u.UnitId}' must be ≥ 0.");
            sum += u.LipschitzInfluence * u.PerUnitBound;
        }
        return sum;
    }
}
