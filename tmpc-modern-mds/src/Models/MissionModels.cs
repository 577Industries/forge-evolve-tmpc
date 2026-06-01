// Immutable domain records for the modernized mission processor.
//
// These replace the legacy pattern of parallel List<double> lats/lons and loose locals
// threaded through one ~200-line god method. They are init-only records (value semantics),
// nullable-enabled, and carry no behavior — behavior lives in the single-responsibility
// services. Field shapes mirror the legacy MissionRequest / MissionResult JSON exactly so the
// emitted output is byte-comparable to the legacy `legacyOutput` answer key.

namespace ForgeEvolve.ModernMds.Models;

/// <summary>A single waypoint (decimal degrees). Immutable.</summary>
public sealed record Waypoint(double LatDeg, double LonDeg);

/// <summary>
/// A parsed mission request. Replaces the legacy parallel-array + loose-local parse state.
/// </summary>
public sealed record MissionRequest
{
    public required string MissionId { get; init; }
    public required string Platform { get; init; }
    public required string Variant { get; init; }
    public required long LaunchEpochSec { get; init; }
    public required long DesiredTotEpochSec { get; init; }
    public required IReadOnlyList<Waypoint> Waypoints { get; init; }
}

/// <summary>
/// The computed mission result. Field order/shape matches the legacy MissionResult JSON so the
/// serialized output is directly comparable to the corpus `legacyOutput`.
/// </summary>
public sealed record MissionResult
{
    public required string MissionId { get; init; }
    public required IReadOnlyList<double> LegDistancesNm { get; init; }
    public required double TotalDistanceNm { get; init; }
    public required bool RouteValid { get; init; }
    public required long EstimatedTotEpochSec { get; init; }
    public required bool TotFeasible { get; init; }
    public required bool TaskingGoNoGo { get; init; }
    public required IReadOnlyList<string> Messages { get; init; }
}

/// <summary>Output of route validation: the GO/NO-GO flag plus any per-leg diagnostic messages.</summary>
public sealed record RouteValidation(bool RouteValid, IReadOnlyList<string> Messages);

/// <summary>Output of distance computation: per-leg distances (D1+D2 preserved) and their sum.</summary>
public sealed record DistanceResult(IReadOnlyList<double> LegDistancesNm, double TotalDistanceNm);

/// <summary>Output of the time-on-target estimator (D3 preserved): the estimate and feasibility.</summary>
public sealed record TotResult(long EstimatedTotEpochSec, bool TotFeasible);
