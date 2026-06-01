// Distance responsibility, extracted from the legacy god method.
//
// BEHAVIOR-PRESERVING — reproduces D1 and D2 EXACTLY:
//   D1: the legacy equirectangular kernel uses the RAW (unwrapped) longitude delta
//       (GeoMath.LegLegacyDistanceNm), so anti-meridian legs are wildly wrong.
//   D2: each leg is Math.Round(d, LegacyLegDecimals, MidpointRounding.ToEven) BEFORE being added
//       to the running total (naive left-to-right accumulation), so error accumulates.
// These are PRESERVED, not fixed — they are surfaced as ECP-recommended findings (see README).

using ForgeEvolve.ModernMds.Geometry;
using ForgeEvolve.ModernMds.Models;

namespace ForgeEvolve.ModernMds.Geometry;

/// <summary>Computes per-leg and total distance. Single responsibility.</summary>
public interface IDistanceCalculator
{
    DistanceResult Compute(IReadOnlyList<Waypoint> waypoints, MissionOptions options);
}

/// <inheritdoc />
public sealed class DistanceCalculator : IDistanceCalculator
{
    public DistanceResult Compute(IReadOnlyList<Waypoint> waypoints, MissionOptions options)
    {
        var legDistances = new List<double>();
        double total = 0.0;
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            var leg = new Leg(
                waypoints[i].LatDeg, waypoints[i].LonDeg,
                waypoints[i + 1].LatDeg, waypoints[i + 1].LonDeg);

            // D1: raw unwrapped dlon kernel.
            double d = GeoMath.LegLegacyDistanceNm(options.EarthRadiusNm, leg);

            // D2: banker's round each leg BEFORE summing (matches Python round / C# Math.Round).
            d = Math.Round(d, options.LegacyLegDecimals, MidpointRounding.ToEven);

            legDistances.Add(d);
            total += d; // naive left-to-right accumulation (part of D2 drift)
        }
        return new DistanceResult(legDistances, total);
    }
}
