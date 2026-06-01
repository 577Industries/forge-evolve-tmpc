// FORGE EVOLVE for TMPC — the differential behavioral-equivalence validator (Stage 4).
//
// IEquivalenceValidator.Verify runs the legacy AND modern implementations on every input
// vector and compares their outputs through the mission-data-aware oracles (Oracles.cs). The
// reference answer key carried in EquivalenceTestVector.ExpectedOutputJson (the corpus
// `referenceOutput`) is the tie-breaker that distinguishes a real regression from an
// INTENTIONAL DIVERGENCE (modern corrected a known legacy bug — see governance/pre-registration.md
// "Intentional-divergence policy").
//
// A VECTOR PASSES iff every oracle on it votes Equivalent (no violations, no intentional
// divergence — i.e. legacy and modern agree exactly within tolerance). A vector with an
// intentional divergence is NOT a pass and NOT a violation: it is a finding, counted separately.
// A vector with any UNEXPECTED violation increments Violations (the target is 0).
//
// The headline equivalence number for the REAL modern component is produced at integration (P3);
// here we prove the ENGINE is correct on the corpus + the surrogate legacy.

using ForgeEvolve.Contracts;

namespace ForgeEvolve.Validation;

/// <summary>
/// Differential + mission-data-aware equivalence validator. Stateless and thread-safe; a single
/// instance may verify many units.
/// </summary>
public sealed class EquivalenceValidator : IEquivalenceValidator
{
    /// <inheritdoc/>
    public EquivalenceReport Verify(
        string unitId,
        ILegacyRunner legacy,
        IModernRunner modern,
        IReadOnlyList<EquivalenceTestVector> vectors,
        ToleranceConfig tolerance)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(modern);
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(tolerance);

        // One accumulator per named mission output (declared in report order).
        var routeValid = new OracleAccumulator(Oracles.Names.RouteValid, OracleKind.Discrete);
        var tasking = new OracleAccumulator(Oracles.Names.TaskingGoNoGo, OracleKind.Discrete);
        var totFeasible = new OracleAccumulator(Oracles.Names.TotFeasible, OracleKind.Discrete);
        var estTot = new OracleAccumulator(Oracles.Names.EstimatedTotEpochSec, OracleKind.Discrete);
        var wpCount = new OracleAccumulator(Oracles.Names.WaypointCount, OracleKind.Discrete);
        var msgCount = new OracleAccumulator(Oracles.Names.MessageCount, OracleKind.Discrete);
        var legDist = new OracleAccumulator(Oracles.Names.LegDistancesNm, OracleKind.Continuous);
        var totalDist = new OracleAccumulator(Oracles.Names.TotalDistanceNm, OracleKind.Continuous);

        // Tag-keyed property oracles (anti-meridian / leap-second / precision).
        var antiMeridian = new OracleAccumulator(Oracles.Names.AntiMeridianProperty, OracleKind.Continuous);
        var leapSecond = new OracleAccumulator(Oracles.Names.LeapSecondProperty, OracleKind.Discrete);
        var precision = new OracleAccumulator(Oracles.Names.PrecisionProperty, OracleKind.Continuous);

        int vectorsTotal = vectors.Count;
        int vectorsPassed = 0;
        int violations = 0;                 // UNEXPECTED violations across all oracles
        int intentionalDivergentVectors = 0; // vectors with ≥1 intentional divergence, 0 violations

        foreach (var vec in vectors)
        {
            // Run BOTH implementations on the same input (the differential step).
            MissionResult lRes = MissionResult.Parse(SafeRun(() => legacy.Run(vec.InputJson)));
            MissionResult mRes = MissionResult.Parse(SafeRun(() => modern.Run(vec.InputJson)));
            // The reference answer key (correct semantics) for intent classification.
            MissionResult rRes = MissionResult.Parse(vec.ExpectedOutputJson);

            bool anyViolation = false;
            bool anyIntentional = false;

            void Discrete(OracleAccumulator acc, OracleVote v)
            {
                acc.Add(v);
                if (v == OracleVote.UnexpectedViolation) { anyViolation = true; violations++; }
                else if (v == OracleVote.IntentionalDivergence) anyIntentional = true;
            }

            void Continuous(OracleAccumulator acc, (OracleVote vote, double relErr) r)
            {
                acc.Add(r.vote, double.IsFinite(r.relErr) ? r.relErr : 0.0);
                if (r.vote == OracleVote.UnexpectedViolation) { anyViolation = true; violations++; }
                else if (r.vote == OracleVote.IntentionalDivergence) anyIntentional = true;
            }

            // A modern result that failed to parse is a hard, unexpected failure on every output.
            if (!mRes.Parsed)
            {
                Discrete(routeValid, OracleVote.UnexpectedViolation);
                Discrete(tasking, OracleVote.UnexpectedViolation);
                Discrete(totFeasible, OracleVote.UnexpectedViolation);
                Discrete(estTot, OracleVote.UnexpectedViolation);
                Discrete(wpCount, OracleVote.UnexpectedViolation);
                Discrete(msgCount, OracleVote.UnexpectedViolation);
                Continuous(legDist, (OracleVote.UnexpectedViolation, double.PositiveInfinity));
                Continuous(totalDist, (OracleVote.UnexpectedViolation, double.PositiveInfinity));
            }
            else
            {
                Discrete(routeValid, Oracles.RouteValid(lRes, mRes, rRes));
                Discrete(tasking, Oracles.TaskingGoNoGo(lRes, mRes, rRes));
                Discrete(totFeasible, Oracles.TotFeasible(lRes, mRes, rRes));
                Discrete(estTot, Oracles.EstimatedTot(lRes, mRes, rRes));
                Discrete(wpCount, Oracles.WaypointCount(lRes, mRes, rRes));
                Discrete(msgCount, Oracles.MessageCount(lRes, mRes, rRes));
                Continuous(legDist, Oracles.LegDistances(lRes, mRes, rRes, tolerance));
                Continuous(totalDist, Oracles.TotalDistance(lRes, mRes, rRes, tolerance));
            }

            // ── Explicit PROPERTY oracles, evaluated ONLY on their tagged sub-corpus ──
            if (mRes.Parsed)
            {
                if (HasTag(vec, "anti-meridian"))
                    Continuous(antiMeridian, Oracles.LegDistances(lRes, mRes, rRes, tolerance));
                if (HasTag(vec, "leap-second"))
                    Discrete(leapSecond, Oracles.EstimatedTot(lRes, mRes, rRes));
                if (HasTag(vec, "precision-drift"))
                    Continuous(precision, Oracles.TotalDistance(lRes, mRes, rRes, tolerance));
            }

            if (anyViolation) { /* counted; not a pass */ }
            else if (anyIntentional) intentionalDivergentVectors++;
            else vectorsPassed++;
        }

        // Chernoff bound: valid only in the zero-violation regime, over vectors that PASSED
        // (fully equivalent). With unexpected violations present the empirical rate dominates.
        double chernoff = violations == 0
            ? EquivalenceBounds.ChernoffDeviationBound(vectorsPassed)
            : (double)violations / Math.Max(vectorsTotal, 1);

        var oracles = new List<OracleResult>
        {
            routeValid.ToResult(), tasking.ToResult(), totFeasible.ToResult(),
            estTot.ToResult(), wpCount.ToResult(), msgCount.ToResult(),
            legDist.ToResult(), totalDist.ToResult(),
        };
        // Only surface a property oracle if its tagged sub-corpus was actually present.
        if (antiMeridian.Evaluated > 0) oracles.Add(antiMeridian.ToResult());
        if (leapSecond.Evaluated > 0) oracles.Add(leapSecond.ToResult());
        if (precision.Evaluated > 0) oracles.Add(precision.ToResult());

        var notes = new List<string>
        {
            $"Differential equivalence over {vectorsTotal} vectors (legacy vs modern, mission-data-aware oracles).",
            $"VectorsPassed (fully equivalent, zero divergence): {vectorsPassed}.",
            $"IntentionalDivergences (modern corrected a known legacy bug; findings, not failures): {intentionalDivergentVectors}.",
            $"UnexpectedViolations (target 0): {violations}.",
            violations == 0
                ? $"ChernoffDeviationBound = ln(1/{EquivalenceBounds.PreRegisteredDelta})/N with N={vectorsPassed} (zero-violation regime)."
                : $"ChernoffDeviationBound replaced by empirical deviation rate {violations}/{vectorsTotal} (violations present; bound invalid).",
        };

        return new EquivalenceReport
        {
            UnitId = unitId,
            VectorsTotal = vectorsTotal,
            VectorsPassed = vectorsPassed,
            Violations = violations,
            Oracles = oracles,
            ChernoffDeviationBound = chernoff,
            ConfidenceLevel = EquivalenceBounds.PreRegisteredDelta,
            ComposedSystemBound = null, // populated at system integration (P3) across the unit DAG
            Notes = notes,
        };
    }

    /// <summary>Number of vectors the corpus answer key marks as intentionally divergent,
    /// reproduced by the engine. Exposed for the report/console; recomputed from the vectors.</summary>
    public static bool HasTag(EquivalenceTestVector vec, string tag)
    {
        for (int i = 0; i < vec.Tags.Count; i++)
            if (string.Equals(vec.Tags[i], tag, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Run an implementation defensively: a thrown exception becomes a null output,
    /// which parses to an unparsed result and is scored as an unexpected violation.</summary>
    private static string? SafeRun(Func<string> run)
    {
        try { return run(); }
        catch { return null; }
    }
}
