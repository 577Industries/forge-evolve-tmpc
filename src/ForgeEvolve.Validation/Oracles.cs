// FORGE EVOLVE for TMPC — mission-data-aware equivalence oracles.
//
// An ORACLE answers one question: "for this named mission output, is the modern result
// equivalent to the legacy result?" Two kinds, per the frozen contract (OracleKind):
//
//   * DISCRETE  — exact equality required (tolerance 0). Used for the categorical mission
//                 decisions and the structural counts: routeValid, taskingGoNoGo, totFeasible,
//                 estimatedTotEpochSec, waypointCount, messageCount.
//   * CONTINUOUS— bounded RELATIVE error allowed (from ToleranceConfig). Used for the
//                 floating-point distance/time fields: legDistancesNm[i], totalDistanceNm.
//
// Plus three EXPLICIT PROPERTY oracles keyed off corpus tags — anti-meridian (legs crossing
// ±180), leap-second, and precision (many short legs) — which assert the *specific* defect
// class is exercised and correctly classified on its tagged sub-corpus.
//
// Each per-vector comparison returns an OracleVote: equivalent, an unexpected violation, or an
// INTENTIONAL DIVERGENCE (modern differs from legacy, but legacy is the one that disagrees with
// the corpus reference answer — i.e. modern fixed a known legacy bug). The accumulator folds
// votes into the frozen OracleResult DTO.

using ForgeEvolve.Contracts;

namespace ForgeEvolve.Validation;

/// <summary>The classification of a single per-vector oracle comparison.</summary>
public enum OracleVote
{
    /// <summary>Modern == legacy within the oracle's tolerance.</summary>
    Equivalent,
    /// <summary>Modern != legacy AND legacy agrees with the reference answer key — a real defect.</summary>
    UnexpectedViolation,
    /// <summary>Modern != legacy because legacy is the one wrong vs. the reference (a finding).</summary>
    IntentionalDivergence,
}

/// <summary>
/// Mutable per-oracle accumulator. The validator creates one per named output, feeds it each
/// vector's (legacy, modern, reference) triple, then snapshots it into an immutable
/// <see cref="OracleResult"/>.
/// </summary>
public sealed class OracleAccumulator
{
    public string Name { get; }
    public OracleKind Kind { get; }

    private int _evaluated;
    private int _violations;
    private int _intentional;
    private double _maxRelError;

    public OracleAccumulator(string name, OracleKind kind)
    {
        Name = name;
        Kind = kind;
    }

    public int Evaluated => _evaluated;
    public int Violations => _violations;
    public int IntentionalDivergences => _intentional;
    public double MaxObservedRelativeError => _maxRelError;

    /// <summary>Fold one vote (with the relative error observed, if continuous).</summary>
    public void Add(OracleVote vote, double relError = 0.0)
    {
        _evaluated++;
        if (relError > _maxRelError) _maxRelError = relError;
        switch (vote)
        {
            case OracleVote.UnexpectedViolation: _violations++; break;
            case OracleVote.IntentionalDivergence: _intentional++; break;
        }
    }

    /// <summary>Snapshot into the frozen contract DTO.</summary>
    public OracleResult ToResult() => new()
    {
        OracleName = Name,
        Kind = Kind,
        VectorsEvaluated = _evaluated,
        Violations = _violations,
        MaxObservedRelativeError = _maxRelError,
        IsIntentionalDivergence = _intentional > 0,
    };
}

/// <summary>
/// The mission-data-aware oracle definitions. Each oracle knows how to pull its named output
/// from a <see cref="MissionResult"/> and how to vote on one (legacy, modern, reference) triple.
/// </summary>
public static class Oracles
{
    /// <summary>Standard mission-result oracle names (ordering = report ordering).</summary>
    public static class Names
    {
        public const string RouteValid = "routeValid";
        public const string TaskingGoNoGo = "taskingGoNoGo";
        public const string TotFeasible = "totFeasible";
        public const string EstimatedTotEpochSec = "estimatedTotEpochSec";
        public const string WaypointCount = "waypointCount";
        public const string MessageCount = "messageCount";
        public const string LegDistancesNm = "legDistancesNm";
        public const string TotalDistanceNm = "totalDistanceNm";

        // Tag-keyed property oracles.
        public const string AntiMeridianProperty = "property:anti-meridian";
        public const string LeapSecondProperty = "property:leap-second";
        public const string PrecisionProperty = "property:precision";
    }

    /// <summary>Relative error with an absolute floor to avoid divide-by-zero on near-zero
    /// values, matching reference.py <c>_rel_diff</c> and tools/LegacyCheck.RelErr.</summary>
    public static double RelativeError(double a, double b, double floor)
    {
        double denom = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Max(floor, 1e-12));
        return Math.Abs(a - b) / denom;
    }

    // ── DISCRETE oracle votes ──────────────────────────────────────────────────────
    //
    // A discrete output is equivalent iff modern == legacy EXACTLY. When they differ, we ask the
    // reference answer key who is right:
    //   * legacy == reference, modern != reference  -> modern regressed: UNEXPECTED VIOLATION.
    //   * legacy != reference, modern == reference  -> modern fixed a legacy bug: INTENTIONAL.
    //   * legacy != reference, modern != reference  -> both wrong, modern still wrong: VIOLATION.

    private static OracleVote VoteDiscrete<T>(T legacy, T modern, T reference)
        where T : IEquatable<T>
    {
        if (modern.Equals(legacy)) return OracleVote.Equivalent;
        // Modern disagrees with legacy. Intentional only if modern now matches the reference
        // truth AND legacy was the one that was wrong.
        if (modern.Equals(reference) && !legacy.Equals(reference))
            return OracleVote.IntentionalDivergence;
        return OracleVote.UnexpectedViolation;
    }

    // ── CONTINUOUS oracle votes ────────────────────────────────────────────────────
    //
    // Equivalent iff relErr(modern, legacy) <= bound. When out of tolerance, the reference
    // decides intent: if modern is within tolerance of the reference but legacy is NOT, the
    // modern simply corrected a legacy numeric defect (D1/D2) -> intentional divergence.

    private static (OracleVote vote, double relErr) VoteContinuous(
        double legacy, double modern, double reference, ToleranceConfig tol)
    {
        double floor = tol.ContinuousAbsoluteFloor;
        double bound = tol.ContinuousRelativeError;
        double relLegacyModern = RelativeError(legacy, modern, floor);
        if (relLegacyModern <= bound)
            return (OracleVote.Equivalent, relLegacyModern);

        double relModernRef = RelativeError(modern, reference, floor);
        double relLegacyRef = RelativeError(legacy, reference, floor);
        if (relModernRef <= bound && relLegacyRef > bound)
            return (OracleVote.IntentionalDivergence, relLegacyModern);

        return (OracleVote.UnexpectedViolation, relLegacyModern);
    }

    // ── Public per-oracle comparison entry points used by the validator ─────────────

    public static OracleVote RouteValid(MissionResult l, MissionResult m, MissionResult r)
        => VoteDiscrete(l.RouteValid, m.RouteValid, r.RouteValid);

    public static OracleVote TaskingGoNoGo(MissionResult l, MissionResult m, MissionResult r)
        => VoteDiscrete(l.TaskingGoNoGo, m.TaskingGoNoGo, r.TaskingGoNoGo);

    public static OracleVote TotFeasible(MissionResult l, MissionResult m, MissionResult r)
        => VoteDiscrete(l.TotFeasible, m.TotFeasible, r.TotFeasible);

    public static OracleVote EstimatedTot(MissionResult l, MissionResult m, MissionResult r)
        => VoteDiscrete(l.EstimatedTotEpochSec, m.EstimatedTotEpochSec, r.EstimatedTotEpochSec);

    public static OracleVote WaypointCount(MissionResult l, MissionResult m, MissionResult r)
        => VoteDiscrete(l.WaypointCount, m.WaypointCount, r.WaypointCount);

    public static OracleVote MessageCount(MissionResult l, MissionResult m, MissionResult r)
        => VoteDiscrete(l.MessageCount, m.MessageCount, r.MessageCount);

    /// <summary>
    /// Continuous oracle over the whole <c>legDistancesNm</c> array. Returns the worst per-leg
    /// vote (violation &gt; intentional &gt; equivalent) and the max relative error. A length
    /// mismatch is an unparseable structural defect -&gt; unexpected violation at relErr=+inf.
    /// </summary>
    public static (OracleVote vote, double relErr) LegDistances(
        MissionResult l, MissionResult m, MissionResult r, ToleranceConfig tol)
    {
        if (m.LegDistancesNm.Count != l.LegDistancesNm.Count)
            return (OracleVote.UnexpectedViolation, double.PositiveInfinity);

        OracleVote worst = OracleVote.Equivalent;
        double maxRel = 0.0;
        for (int i = 0; i < l.LegDistancesNm.Count; i++)
        {
            double refVal = i < r.LegDistancesNm.Count ? r.LegDistancesNm[i] : double.NaN;
            var (vote, rel) = VoteContinuous(l.LegDistancesNm[i], m.LegDistancesNm[i], refVal, tol);
            if (double.IsFinite(rel) && rel > maxRel) maxRel = rel;
            worst = Worse(worst, vote);
        }
        return (worst, maxRel);
    }

    public static (OracleVote vote, double relErr) TotalDistance(
        MissionResult l, MissionResult m, MissionResult r, ToleranceConfig tol)
        => VoteContinuous(l.TotalDistanceNm, m.TotalDistanceNm, r.TotalDistanceNm, tol);

    /// <summary>Severity order so an array oracle reports its worst leg.</summary>
    public static OracleVote Worse(OracleVote a, OracleVote b)
    {
        int Rank(OracleVote v) => v switch
        {
            OracleVote.UnexpectedViolation => 2,
            OracleVote.IntentionalDivergence => 1,
            _ => 0,
        };
        return Rank(a) >= Rank(b) ? a : b;
    }
}
