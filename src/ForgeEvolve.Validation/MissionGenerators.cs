// FORGE EVOLVE for TMPC — CsCheck generators for property-based equivalence testing.
//
// These produce random, well-formed MissionRequest JSON inputs (the same schema the legacy
// MissionProcessor and reference model consume) plus helpers to build perturbed-modern stand-
// ins. The property tests (in the test project) use them to assert the engine's invariants on
// thousands of random missions, beyond the frozen corpus:
//
//   * discrete oracles NEVER report a violation when modern == legacy (byte-identical output);
//   * perturbing a continuous output beyond tolerance IS caught (a violation appears).
//
// Generation deliberately favors "nominal" geometry (small legs, no anti-meridian crossing) so
// most random missions are equivalence-preserving; the perturbation tests inject the breakage
// explicitly. No real data — purely synthetic fixtures.

using System.Globalization;
using System.Text;
using System.Text.Json;
using CsCheck;

namespace ForgeEvolve.Validation;

/// <summary>CsCheck generators + perturbation helpers for random mission inputs.</summary>
public static class MissionGenerators
{
    private static readonly string[] Platforms = { "DDG", "CG", "SSN" };
    private static readonly string[] Variants = { "BlockIV", "BlockV", "MST" };

    /// <summary>
    /// Generate a random, well-formed MissionRequest JSON string with "nominal" geometry:
    /// 2–8 waypoints, small inter-waypoint deltas (within the degree-box so routes are usually
    /// valid), longitudes kept away from the ±180 anti-meridian so the legacy/modern distance
    /// math agrees. This is the equivalence-preserving regime used to assert discrete-oracle
    /// stability.
    /// </summary>
    public static Gen<string> NominalMissionJson { get; } =
        from idn in Gen.Int[0, 999_999]
        from plat in Gen.OneOfConst(Platforms)
        from var_ in Gen.OneOfConst(Variants)
        from launch in Gen.Long[1_000_500_000L, 1_999_000_000L]
        from totOffset in Gen.Int[-5000, 5000]
        from nWp in Gen.Int[2, 8]
        from lat0 in Gen.Double[-55.0, 55.0]
        from lon0 in Gen.Double[-150.0, 150.0]   // away from ±180 so no anti-meridian crossing
        from dlat in Gen.Double[-10.0, 10.0].Array[nWp - 1]
        from dlon in Gen.Double[-10.0, 10.0].Array[nWp - 1]
        select BuildMissionJson(idn, plat, var_, launch, launch + totOffset,
            BuildWaypoints(lat0, lon0, dlat, dlon));

    private static (double lat, double lon)[] BuildWaypoints(
        double lat0, double lon0, double[] dlat, double[] dlon)
    {
        var pts = new (double, double)[dlat.Length + 1];
        double lat = Math.Clamp(lat0, -85.0, 85.0);
        double lon = Math.Clamp(lon0, -170.0, 170.0);
        pts[0] = (lat, lon);
        for (int i = 0; i < dlat.Length; i++)
        {
            lat = Math.Clamp(lat + dlat[i], -85.0, 85.0);
            lon = Math.Clamp(lon + dlon[i], -170.0, 170.0); // stay clear of ±180
            pts[i + 1] = (lat, lon);
        }
        return pts;
    }

    private static string BuildMissionJson(int idn, string platform, string variant,
        long launch, long desiredTot, (double lat, double lon)[] waypoints)
    {
        var sb = new StringBuilder(256);
        sb.Append('{');
        sb.Append("\"missionId\":\"SYN-GEN-").Append(idn.ToString("D6", CultureInfo.InvariantCulture)).Append("\",");
        sb.Append("\"platform\":\"").Append(platform).Append("\",");
        sb.Append("\"variant\":\"").Append(variant).Append("\",");
        sb.Append("\"launchEpochSec\":").Append(launch.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"desiredTotEpochSec\":").Append(desiredTot.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"waypoints\":[");
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"latDeg\":").Append(waypoints[i].lat.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"lonDeg\":").Append(waypoints[i].lon.ToString("R", CultureInfo.InvariantCulture)).Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>
    /// Perturb a mission-result JSON by scaling <c>totalDistanceNm</c> (and the first leg) by
    /// <paramref name="factor"/>. Used to build a "broken modern" whose continuous output is
    /// pushed beyond tolerance — the engine must catch it as a violation. A factor like 1.01
    /// (1% error) is many orders of magnitude past the 1e-9 relative bound.
    /// </summary>
    public static string PerturbContinuous(string resultJson, double factor)
    {
        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;
        var buffer = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("totalDistanceNm"))
                {
                    w.WriteNumber("totalDistanceNm", prop.Value.GetDouble() * factor);
                }
                else if (prop.NameEquals("legDistancesNm"))
                {
                    w.WriteStartArray("legDistancesNm");
                    int i = 0;
                    foreach (var leg in prop.Value.EnumerateArray())
                    {
                        double v = leg.GetDouble();
                        w.WriteNumberValue(i == 0 ? v * factor : v);
                        i++;
                    }
                    w.WriteEndArray();
                }
                else
                {
                    prop.WriteTo(w);
                }
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Flip the <c>totFeasible</c> boolean in a mission-result JSON (used to prove the discrete
    /// totFeasible oracle has tolerance 0 — a single flip must be caught).
    /// </summary>
    public static string FlipTotFeasible(string resultJson)
    {
        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;
        var buffer = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("totFeasible"))
                    w.WriteBoolean("totFeasible", !prop.Value.GetBoolean());
                else
                    prop.WriteTo(w);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
