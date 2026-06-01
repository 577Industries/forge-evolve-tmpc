// Route-validation responsibility, extracted from the legacy god method.
//
// BEHAVIOR-PRESERVING and BUG-FREE-SHARED: this is the degree-box + turn-rate validation that the
// reference and legacy share verbatim, so routeValid is preserved EXACTLY. Message ordering matches
// the legacy: ALL "LEG_OUT_OF_BOX:i" messages (the per-leg loop) are appended first, then ALL
// "TURN_EXCEEDED:(i+1)" messages (the bearings loop). The shared, wrapped-dlon geometry is used in
// both legacy and modern here — D1 does NOT apply to validation.

using System.Globalization;
using ForgeEvolve.ModernMds.Geometry;
using ForgeEvolve.ModernMds.Models;

namespace ForgeEvolve.ModernMds.Routing;

/// <summary>Validates a route's legs (degree box) and turns (turn-rate). Single responsibility.</summary>
public interface IRouteValidator
{
    RouteValidation Validate(IReadOnlyList<Waypoint> waypoints, MissionOptions options);
}

/// <inheritdoc />
public sealed class RouteValidator : IRouteValidator
{
    public RouteValidation Validate(IReadOnlyList<Waypoint> waypoints, MissionOptions options)
    {
        bool valid = true;
        var messages = new List<string>();
        var bearings = new List<double>();

        valid &= CheckLegBoxes(waypoints, options, messages, bearings);
        valid &= CheckTurns(bearings, options, messages);

        return new RouteValidation(valid, messages);
    }

    /// <summary>Per-leg degree-box check; also accumulates bearings for the turn check.</summary>
    private static bool CheckLegBoxes(
        IReadOnlyList<Waypoint> waypoints,
        MissionOptions options,
        List<string> messages,
        List<double> bearings)
    {
        bool valid = true;
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            var leg = ToLeg(waypoints, i);
            double dLat = Math.Abs(leg.Lat2 - leg.Lat1);
            double dLonWrapped = Math.Abs(GeoMath.WrapDlon(leg.Lon2 - leg.Lon1));
            if (dLat > options.MaxLegDLatDeg || dLonWrapped > options.MaxLegDLonDeg)
            {
                valid = false;
                messages.Add("LEG_OUT_OF_BOX:" + i.ToString(CultureInfo.InvariantCulture));
            }
            bearings.Add(GeoMath.InitialBearingDeg(leg));
        }
        return valid;
    }

    /// <summary>Consecutive-bearing turn-rate check.</summary>
    private static bool CheckTurns(
        IReadOnlyList<double> bearings,
        MissionOptions options,
        List<string> messages)
    {
        bool valid = true;
        for (int i = 0; i < bearings.Count - 1; i++)
        {
            double turn = GeoMath.TurnAngleDeg(bearings[i], bearings[i + 1]);
            if (turn > options.MaxTurnDeg)
            {
                valid = false;
                messages.Add("TURN_EXCEEDED:" + (i + 1).ToString(CultureInfo.InvariantCulture));
            }
        }
        return valid;
    }

    private static Leg ToLeg(IReadOnlyList<Waypoint> waypoints, int i) =>
        new(waypoints[i].LatDeg, waypoints[i].LonDeg, waypoints[i + 1].LatDeg, waypoints[i + 1].LonDeg);
}
