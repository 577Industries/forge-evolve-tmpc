// FORGE EVOLVE for TMPC — statistical equivalence bounds.
//
// Two quantities, both pre-registered in governance/pre-registration.md:
//
//   1. The per-unit "rule-of-three" UPPER CONFIDENCE BOUND on the deviation probability.
//   2. The system-level COMPOSED bound from the Equivalence-Composability theorem.
//
// ── 1. Rule-of-three upper confidence bound ─────────────────────────────────────────
//
// We observe N independent mission vectors and find ZERO behavioral violations (every vector
// is equivalent within tolerance, intentional divergences excluded). We want an HONEST upper
// confidence bound on the true per-mission operational-deviation probability p.
//
// Let X be the count of deviating missions in N i.i.d. trials, X ~ Binomial(N, p). We observed
// X = 0. The exact one-sided Clopper–Pearson upper bound at confidence (1 − α) solves
//      Pr[X = 0 | p] = (1 − p)^N = α   ⇒   p_upper = 1 − α^(1/N).
// Using the standard (slightly conservative) bound (1 − p)^N ≤ e^{−pN} and requiring the
// probability of "seeing zero events when the true rate is ≥ p" to be at most α gives the
// closed-form RULE OF THREE:
//
//      p ≤ ln(1/α) / N            with α = 1 − ConfidenceLevel        (the published statistic)
//
// This is the (1 − α) = ConfidenceLevel UPPER confidence bound on the per-vector deviation
// probability. At 95% it is ln(1/0.05)/N = ln(20)/N ≈ 3/N (hence "rule of three"); for the full
// N = 2000 corpus with zero violations that is ln(20)/2000 ≈ 1.498e-3. Lower is better; it
// shrinks as 1/N.
//
// IMPORTANT (the bug this file fixes): the confidence parameter passed in IS the CONFIDENCE,
// not its complement. A higher confidence (closer to 1) means a LARGER, more conservative bound,
// because demanding more confidence widens the interval:
//      95%   → ln(1/0.05 )/2000 = ln(20  )/2000 ≈ 1.498e-3   (PRIMARY, headline)
//      99%   → ln(1/0.01 )/2000 = ln(100 )/2000 ≈ 2.303e-3
//      99.9% → ln(1/0.001)/2000 = ln(1000)/2000 ≈ 3.454e-3   (secondary, more conservative)
// The previous code computed ln(1/δ)/N with δ = 0.999, i.e. ln(1/0.999)/2000 ≈ 5.003e-7, and
// mislabeled it as "the 99.9% bound." That value is the bound at the 0.1% confidence level, NOT
// 99.9% — it understated risk by ~four orders of magnitude. It has been corrected to the
// rule-of-three upper confidence bound above. If ANY unexpected violation is observed, the
// empirical deviation rate (violations / N) dominates and is reported instead — the bound is
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
// where b_u is unit u's per-unit upper confidence bound and L_u ≥ 0 is the Lipschitz INFLUENCE
// COEFFICIENT of unit u on observable system outputs (how strongly a deviation inside u can
// propagate to a system-visible deviation; L_u = 1 for an output-facing unit, < 1 for a unit
// whose errors are attenuated downstream, > 1 for an amplifying unit). The union-bound /
// Lipschitz-composition argument: deviations compose additively to first order, each scaled by
// its propagation gain. This is conservative (a union bound), so it is a true upper bound on
// end-to-end deviation probability. With all L_u = 1 it reduces to the plain sum of per-unit
// bounds (the worst case where every unit faces an output). For the single-unit demo it equals
// the per-unit 95% bound.

namespace ForgeEvolve.Validation;

/// <summary>One validated unit's contribution to the composed-system equivalence bound.</summary>
/// <param name="UnitId">The migration-unit identifier.</param>
/// <param name="PerUnitBound">That unit's per-unit upper confidence bound (ln(1/α)/N_u).</param>
/// <param name="LipschitzInfluence">Lipschitz influence coefficient L_u ≥ 0 (default 1.0).</param>
public readonly record struct UnitBound(string UnitId, double PerUnitBound, double LipschitzInfluence = 1.0);

/// <summary>Closed-form equivalence bounds (rule-of-three per-unit + Equivalence-Composability).</summary>
public static class EquivalenceBounds
{
    /// <summary>
    /// The PRIMARY (headline) confidence level for the reported upper bound: 95%.
    /// The rule-of-three bound at 95% is ≈ 3/N (ln(20)/N).
    /// </summary>
    public const double PrimaryConfidenceLevel = 0.95;

    /// <summary>A secondary, more-conservative confidence level we also expose: 99.9%.</summary>
    public const double SecondaryConfidenceLevel = 0.999;

    /// <summary>
    /// Rule-of-three UPPER CONFIDENCE BOUND on the per-vector deviation probability for ZERO
    /// failures in N trials: <c>ln(1 / (1 − confidenceLevel)) / N</c>. Valid only in the
    /// zero-violation regime (caller guarantees N = vectors passed with zero unexpected
    /// violations). A HIGHER confidence level yields a LARGER (more conservative) bound. Returns
    /// +∞ for N ≤ 0 (no evidence) so it can never understate risk.
    /// </summary>
    /// <param name="vectorsPassedZeroViolation">N — equivalent vectors with zero violations.</param>
    /// <param name="confidenceLevel">
    /// The CONFIDENCE (1 − α), default 95%. At 0.95, N=2000: ln(20)/2000 ≈ 1.498e-3.
    /// </param>
    public static double UpperConfidenceBound(int vectorsPassedZeroViolation,
        double confidenceLevel = PrimaryConfidenceLevel)
    {
        if (vectorsPassedZeroViolation <= 0) return double.PositiveInfinity;
        if (confidenceLevel is <= 0.0 or >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(confidenceLevel),
                "confidenceLevel must be in (0,1).");
        double alpha = 1.0 - confidenceLevel;          // tail probability
        return Math.Log(1.0 / alpha) / vectorsPassedZeroViolation;
    }

    /// <summary>
    /// Equivalence-Composability system bound: <c>Σ_u L_u · b_u</c> — the Lipschitz-influence-
    /// weighted sum of per-unit upper confidence bounds across the migration-unit DAG. A
    /// conservative (union-bound) upper bound on end-to-end operational-deviation probability.
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
