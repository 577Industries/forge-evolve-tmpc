// Time-on-target responsibility, extracted from the legacy god method.
//
// BEHAVIOR-PRESERVING — reproduces D3 EXACTLY:
//   * travel time is cast to long via TRUNCATION toward zero ((long) cast), NOT rounding;
//   * the synthetic leap-second adjustment is OMITTED entirely (the boundaries table is carried in
//     MissionOptions for the ECP-recommended fix but is deliberately NOT applied here).
// estimatedTot = launch + truncate(total / speed); feasible iff |estimatedTot - desiredTot| <= tol.

using ForgeEvolve.ModernMds.Models;

namespace ForgeEvolve.ModernMds.Timing;

/// <summary>Estimates time-on-target and feasibility. Single responsibility.</summary>
public interface ITotEstimator
{
    TotResult Estimate(double totalDistanceNm, long launchEpochSec, long desiredTotEpochSec, MissionOptions options);
}

/// <inheritdoc />
public sealed class TotEstimator : ITotEstimator
{
    public TotResult Estimate(
        double totalDistanceNm,
        long launchEpochSec,
        long desiredTotEpochSec,
        MissionOptions options)
    {
        // D3: truncation toward zero (NOT round), and NO leap-second adjustment.
        long travelSec = (long)(totalDistanceNm / options.NominalSpeedNmPerSec);
        long estimatedTot = launchEpochSec + travelSec;
        bool feasible = Math.Abs(estimatedTot - desiredTotEpochSec) <= options.TotTolSec;
        return new TotResult(estimatedTot, feasible);
    }
}
