// FORGE EVOLVE for TMPC — intentional-divergence detector.
//
// "Intentional divergence" = a vector where the LEGACY output is operationally wrong vs. the
// reference answer key (so a correct modern that matches the reference will *intentionally*
// differ from legacy, and that difference is a FINDING, not an equivalence failure — see
// governance/pre-registration.md "Intentional-divergence policy").
//
// The detector reproduces the corpus answer key's `expectedLegacyDivergent` definition EXACTLY
// (reference/reference.py :: is_legacy_divergent): a vector is divergent iff
//   * any continuous distance field (totalDistanceNm or any legDistancesNm[i]) differs by more
//     than the relative tolerance (D1 anti-meridian / accumulated D2 precision drift), OR
//   * the totFeasible boolean decision flips (D3 truncation + omitted leap seconds), OR
//   * a categorical decision (routeValid / taskingGoNoGo) differs — by construction this never
//     happens (shared bug-free paths), but it is checked defensively.
//
// Detecting divergence requires ONLY the legacy output and the reference answer key — it does
// not depend on the modern implementation — so the detector can be validated against the
// 321-vector ground truth independently of any modern component.
//
// HONESTY DISCLOSURE (read before citing the precision/recall numbers):
//   This detector applies the SAME divergence definition that the corpus used to LABEL its
//   `expectedLegacyDivergent` ground truth (reference/reference.py :: is_legacy_divergent). The
//   detector and the labeler are therefore the same function. Consequently precision = recall =
//   F1 = 1.0 is a measure of ENGINE SELF-CONSISTENCY / CORRECTNESS OF THE IMPLEMENTATION (the C#
//   port faithfully reproduces the reference definition on every vector), NOT a measure of blind
//   detection skill against unseen or independently-authored criteria. It demonstrates that the
//   implementation matches its own specification — it is not evidence of generalization to a
//   different, withheld divergence taxonomy. Cite it accordingly.

using ForgeEvolve.Contracts;

namespace ForgeEvolve.Validation;

/// <summary>Precision/recall of the intentional-divergence detector vs. a labeled ground truth.</summary>
/// <param name="GroundTruthPositives">Vectors the corpus marks divergent (the 321).</param>
/// <param name="DetectedPositives">Vectors the detector flagged divergent.</param>
/// <param name="TruePositives">Flagged AND truly divergent.</param>
/// <param name="FalsePositives">Flagged but NOT truly divergent.</param>
/// <param name="FalseNegatives">Truly divergent but NOT flagged.</param>
public readonly record struct DetectorScore(
    int GroundTruthPositives,
    int DetectedPositives,
    int TruePositives,
    int FalsePositives,
    int FalseNegatives)
{
    /// <summary>TP / (TP + FP); 1.0 when nothing was flagged (vacuously precise).</summary>
    public double Precision => TruePositives + FalsePositives == 0
        ? 1.0 : (double)TruePositives / (TruePositives + FalsePositives);

    /// <summary>TP / (TP + FN); 1.0 when there are no positives to find.</summary>
    public double Recall => TruePositives + FalseNegatives == 0
        ? 1.0 : (double)TruePositives / (TruePositives + FalseNegatives);

    /// <summary>Harmonic mean of precision and recall.</summary>
    public double F1 => Precision + Recall == 0
        ? 0.0 : 2.0 * Precision * Recall / (Precision + Recall);
}

/// <summary>Reproduces the corpus `expectedLegacyDivergent` answer key from (legacy, reference).</summary>
public static class DivergenceDetector
{
    /// <summary>
    /// True iff the legacy output is operationally divergent from the reference answer key,
    /// per reference.py :: is_legacy_divergent. Uses the same relative-error definition and the
    /// same totFeasible-flip rule.
    /// </summary>
    public static bool IsIntentionalDivergence(
        MissionResult legacy, MissionResult reference, ToleranceConfig tolerance)
    {
        if (!legacy.Parsed || !reference.Parsed) return true; // a structural break IS divergent

        double floor = tolerance.ContinuousAbsoluteFloor;
        double bound = tolerance.ContinuousRelativeError;

        // Continuous: totalDistanceNm.
        if (Oracles.RelativeError(reference.TotalDistanceNm, legacy.TotalDistanceNm, floor) > bound)
            return true;

        // Continuous: per-leg distances (length mismatch => divergent).
        if (reference.LegDistancesNm.Count != legacy.LegDistancesNm.Count) return true;
        for (int i = 0; i < reference.LegDistancesNm.Count; i++)
            if (Oracles.RelativeError(reference.LegDistancesNm[i], legacy.LegDistancesNm[i], floor) > bound)
                return true;

        // Boolean decision flip (D3).
        if (reference.TotFeasible != legacy.TotFeasible) return true;

        // Categorical decisions (defensive; preserved by construction).
        if (reference.RouteValid != legacy.RouteValid) return true;
        if (reference.TaskingGoNoGo != legacy.TaskingGoNoGo) return true;

        return false;
    }

    /// <summary>
    /// Convenience overload: detect divergence directly from a vector by running the legacy
    /// implementation on its input and comparing to the vector's reference answer key.
    /// </summary>
    public static bool IsIntentionalDivergence(
        EquivalenceTestVector vec, ILegacyRunner legacy, ToleranceConfig tolerance)
    {
        var lRes = MissionResult.Parse(legacy.Run(vec.InputJson));
        var rRes = MissionResult.Parse(vec.ExpectedOutputJson);
        return IsIntentionalDivergence(lRes, rRes, tolerance);
    }

    /// <summary>
    /// Score the detector against a labeled ground truth. <paramref name="groundTruth"/> gives
    /// the corpus `expectedLegacyDivergent` flag per vector (the 321-vector answer key).
    /// </summary>
    public static DetectorScore Score(
        IReadOnlyList<EquivalenceTestVector> vectors,
        ILegacyRunner legacy,
        IReadOnlyList<bool> groundTruth,
        ToleranceConfig tolerance)
    {
        if (vectors.Count != groundTruth.Count)
            throw new ArgumentException("vectors and groundTruth must be the same length.");

        int gtPos = 0, detPos = 0, tp = 0, fp = 0, fn = 0;
        for (int i = 0; i < vectors.Count; i++)
        {
            bool truth = groundTruth[i];
            bool detected = IsIntentionalDivergence(vectors[i], legacy, tolerance);
            if (truth) gtPos++;
            if (detected) detPos++;
            if (detected && truth) tp++;
            else if (detected && !truth) fp++;
            else if (!detected && truth) fn++;
        }
        return new DetectorScore(gtPos, detPos, tp, fp, fn);
    }
}
