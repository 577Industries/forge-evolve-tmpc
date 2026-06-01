// Pure geometry kernels. Single responsibility: trigonometry only, no I/O, no state.
//
// BEHAVIOR-PRESERVING: these are byte-faithful to the legacy MissionProcessor helpers and the
// Python reference. Two distance kernels exist on purpose:
//   * LegLagacyDistanceNm — the D1 legacy kernel (RAW unwrapped dlon). PRESERVED.
//   * LegCorrectDistanceNm — the wrapped-dlon kernel (the ECP-recommended fix). NOT used by the
//     behavior-preserving path; provided so the ECP finding is concrete and testable.
// The bearing/turn helpers feed the bug-free shared route-validation path and use a wrapped dlon
// in BOTH legacy and modern, exactly as the reference specifies.

namespace ForgeEvolve.ModernMds.Geometry;

/// <summary>Stateless great-circle / equirectangular geometry kernels.</summary>
public static class GeoMath
{
    private const double DegToRad = Math.PI / 180.0;

    /// <summary>Normalize a longitude delta to [-180, 180] (correct anti-meridian handling).</summary>
    public static double WrapDlon(double dlon)
    {
        while (dlon > 180.0) dlon -= 360.0;
        while (dlon < -180.0) dlon += 360.0;
        return dlon;
    }

    /// <summary>
    /// D1 LEGACY leg distance (nm): equirectangular kernel using the RAW (unwrapped) longitude
    /// delta. Anti-meridian legs are wildly wrong by design — PRESERVED for equivalence.
    /// </summary>
    public static double LegLegacyDistanceNm(double earthRadiusNm, in Leg leg)
    {
        double dlonRad = (leg.Lon2 - leg.Lon1) * DegToRad; // D1: NO wrap
        return EquirectangularNm(earthRadiusNm, leg, dlonRad);
    }

    /// <summary>
    /// CORRECT leg distance (nm) with a wrapped longitude delta. This is the ECP-recommended fix
    /// for D1; it is NOT used by the behavior-preserving path (kept here for the finding's tests).
    /// </summary>
    public static double LegCorrectDistanceNm(double earthRadiusNm, in Leg leg)
    {
        double dlonRad = WrapDlon(leg.Lon2 - leg.Lon1) * DegToRad;
        return EquirectangularNm(earthRadiusNm, leg, dlonRad);
    }

    /// <summary>Initial great-circle bearing (deg, 0..360). Uses a WRAPPED dlon (bug-free shared).</summary>
    public static double InitialBearingDeg(in Leg leg)
    {
        double rlat1 = leg.Lat1 * DegToRad;
        double rlat2 = leg.Lat2 * DegToRad;
        double dlon = WrapDlon(leg.Lon2 - leg.Lon1) * DegToRad;
        double y = Math.Sin(dlon) * Math.Cos(rlat2);
        double x = Math.Cos(rlat1) * Math.Sin(rlat2)
                   - Math.Sin(rlat1) * Math.Cos(rlat2) * Math.Cos(dlon);
        double brg = Math.Atan2(y, x) / DegToRad;
        return (brg + 360.0) % 360.0;
    }

    /// <summary>Absolute turn between two bearings, in [0, 180].</summary>
    public static double TurnAngleDeg(double bearing1, double bearing2)
    {
        double d = Math.Abs(bearing2 - bearing1) % 360.0;
        return d > 180.0 ? 360.0 - d : d;
    }

    /// <summary>Shared equirectangular core: d = R * sqrt(x^2 + dlat^2), x = dlon*cos(meanLat).</summary>
    private static double EquirectangularNm(double earthRadiusNm, in Leg leg, double dlonRad)
    {
        double dlatRad = (leg.Lat2 - leg.Lat1) * DegToRad;
        double meanLatRad = ((leg.Lat1 + leg.Lat2) / 2.0) * DegToRad;
        double x = dlonRad * Math.Cos(meanLatRad);
        return earthRadiusNm * Math.Sqrt(x * x + dlatRad * dlatRad);
    }
}

/// <summary>An immutable ordered leg between two waypoints. Value type for cheap passing.</summary>
public readonly record struct Leg(double Lat1, double Lon1, double Lat2, double Lon2);
