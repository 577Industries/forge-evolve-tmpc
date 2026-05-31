// ─────────────────────────────────────────────────────────────────────────────
// MissionProcessor — SYNTHETIC, INTENTIONALLY-LEGACY MDS-like mission processor.
//
// PART OF: FORGE EVOLVE for TMPC, synthetic unclassified surrogate (Phase 1).
//
// This file is engineered to exhibit the *classes of technical debt* the government
// described for the real (1.3M-LOC, mostly-C#) MDS — NOT any real algorithm, data, or
// system. It is 100% synthetic and unclassified. See ../../DEBT.md for the mapping from
// each synthetic debt item to its plausible real-MDS analog.
//
// Deliberate debt embodied here (DO NOT "fix"; the debt is the artifact under study):
//   * GOD CLASS: ProcessMission(...) is one ~200-line method with cyclomatic complexity
//     well above 30 — parse + validate + distance + TOT + tasking + distribute, all
//     inlined, with magic numbers and deep nesting.
//   * STATIC MUTABLE CONFIG: LegacyConfig holds global state (a classic testability and
//     thread-safety smell).
//   * INLINE DATA ACCESS: an ADO.NET "publish" call lives inside the god method, guarded
//     by PublishEnabled=false so it never connects during the demo.
//   * THREE SEEDED DEFECTS (D1/D2/D3) that affect ONLY the continuous outputs; the
//     categorical decisions (routeValid, taskingGoNoGo) use bug-free shared helpers and
//     are preserved exactly vs. the reference.
//
// The arithmetic here is byte-faithful to reference.legacy_model(...) in the Python
// reference; tools/LegacyCheck proves that against the frozen corpus.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient; // inline data-access debt (never connects; see below)

namespace Tmpc.Surrogate.Legacy
{
    /// <summary>
    /// Static mutable configuration — a global-state smell preserved on purpose. The real
    /// MDS surrogate models a "DistributionConfig" singleton mutated at startup and read
    /// deep inside processing code.
    /// </summary>
    public static class LegacyConfig
    {
        // Physical / business magic numbers (duplicated as raw literals inside the god
        // method too — another intentional smell).
        public static double EarthRadiusNm = 3440.065;
        public static double MaxLegNm = 1500.0;          // documented; not all paths use it
        public static double MaxTurnDeg = 120.0;
        public static double NominalSpeedNmPerSec = 0.15;
        public static int TotTolSec = 120;
        public static double MaxLegDLatDeg = 22.0;
        public static double MaxLegDLonDeg = 22.0;
        public static int LegacyLegDecimals = 8;         // D2 rounding precision

        // INLINE-SQL publish guard. When false (the demo default) the publish path builds
        // its command text but NEVER opens a connection. There is NO real database.
        public static bool PublishEnabled = false;
        public static string PublishConnectionString =
            "Server=(localdb)\\SYNTHETIC;Database=SyntheticMissions;Integrated Security=true;TrustServerCertificate=true;";

        // Synthetic leap-second boundaries. FICTIONAL placeholders — NOT real IERS leap
        // seconds. The legacy estimator (D3) OMITS this adjustment entirely; present here
        // only so the (bug-free) reference path and discovery engine can see the table.
        public static long[] SyntheticLeapBoundaries =
            { 1000000000L, 1100000000L, 1200000000L, 2000000000L, 4000000000L };
    }

    /// <summary>
    /// The legacy mission processor. One public entry point, one god method.
    /// </summary>
    public static class MissionProcessor
    {
        /// <summary>
        /// Process a MissionRequest JSON string and return a MissionResult JSON string.
        /// Implements the legacy defects D1 (anti-meridian: raw dlon), D2 (precision: round
        /// each leg before summing), and D3 (TOT: truncate travel time and omit leap
        /// seconds). Deterministic; no network; no DB connection when PublishEnabled=false.
        /// </summary>
        public static string ProcessMission(string inputJson)
        {
            // ── PARSE (inlined, defensive, branchy) ─────────────────────────────────
            string missionId = "";
            string platform = "";
            string variant = "";
            long launchEpochSec = 0;
            long desiredTotEpochSec = 0;
            List<double> lats = new List<double>();
            List<double> lons = new List<double>();
            List<string> messages = new List<string>();
            messages.Add("LEGACY");

            JsonDocument doc = null;
            try
            {
                doc = JsonDocument.Parse(inputJson);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("missionId", out JsonElement midEl))
                    missionId = midEl.GetString();
                if (root.TryGetProperty("platform", out JsonElement pEl))
                    platform = pEl.GetString();
                if (root.TryGetProperty("variant", out JsonElement vEl))
                    variant = vEl.GetString();
                if (root.TryGetProperty("launchEpochSec", out JsonElement leEl))
                    launchEpochSec = leEl.GetInt64();
                if (root.TryGetProperty("desiredTotEpochSec", out JsonElement dtEl))
                    desiredTotEpochSec = dtEl.GetInt64();

                if (root.TryGetProperty("waypoints", out JsonElement wpEl)
                    && wpEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement wp in wpEl.EnumerateArray())
                    {
                        double la = 0, lo = 0;
                        if (wp.TryGetProperty("latDeg", out JsonElement laEl)) la = laEl.GetDouble();
                        if (wp.TryGetProperty("lonDeg", out JsonElement loEl)) lo = loEl.GetDouble();
                        lats.Add(la);
                        lons.Add(lo);
                    }
                }
            }
            catch (Exception ex)
            {
                // Legacy "swallow and emit a message" pattern.
                messages.Add("PARSE_ERROR");
                if (doc != null) doc.Dispose();
                return BuildResultJson(missionId, new List<double>(), 0.0, false,
                    launchEpochSec, false, false,
                    new List<string> { "LEGACY", "PARSE_ERROR" });
            }
            if (doc != null) doc.Dispose();

            int wpCount = lats.Count;

            // ── PRE-VALIDATE / DIAGNOSTICS (inlined legacy bloat; OUTPUT-NEUTRAL) ─────
            // These branches mirror the kind of defensive, mostly-dead diagnostic code
            // that inflates legacy methods. They accumulate into local counters and do NOT
            // change any emitted field (no messages, no outputs) — they exist to model the
            // real cyclomatic complexity of the god method. The discovery engine should see
            // a method with CC well above 30.
            int northern = 0, southern = 0, easternHemi = 0, zeroLegs = 0, suspectCoords = 0;
            for (int i = 0; i < wpCount; i++)
            {
                if (lats[i] >= 0.0) { northern++; } else { southern++; }
                if (lons[i] >= 0.0) { easternHemi++; }
                if (lats[i] > 90.0 || lats[i] < -90.0) { suspectCoords++; }
                if (lons[i] > 180.0 || lons[i] < -180.0) { suspectCoords++; }
                if (i > 0 && lats[i] == lats[i - 1] && lons[i] == lons[i - 1]) { zeroLegs++; }
            }
            string hemiTag;
            if (northern > 0 && southern > 0) { hemiTag = "MIXED"; }
            else if (northern > 0) { hemiTag = "NORTH"; }
            else if (southern > 0) { hemiTag = "SOUTH"; }
            else { hemiTag = "NONE"; }

            // Platform/variant routing class (switch raises CC; output-neutral).
            int routingClass;
            switch (platform)
            {
                case "DDG": routingClass = 1; break;
                case "CG": routingClass = 2; break;
                case "SSN": routingClass = 3; break;
                default: routingClass = 0; break;
            }
            if (variant == "MST" && routingClass == 3) { routingClass = -3; }
            else if (variant == "BlockIV") { routingClass += 10; }
            else if (variant == "BlockV") { routingClass += 20; }

            // ── VALIDATE (SHARED, BUG-FREE degree-box + turn-rate) ───────────────────
            // Identical to the reference; routeValid is preserved exactly.
            bool routeValid = true;
            List<double> bearings = new List<double>();
            for (int i = 0; i < wpCount - 1; i++)
            {
                double dLat = Math.Abs(lats[i + 1] - lats[i]);
                double dLonWrapped = Math.Abs(WrapDlon(lons[i + 1] - lons[i]));
                bool latOver = dLat > LegacyConfig.MaxLegDLatDeg;
                bool lonOver = dLonWrapped > LegacyConfig.MaxLegDLonDeg;
                if (latOver || lonOver)
                {
                    routeValid = false;
                    messages.Add("LEG_OUT_OF_BOX:" + i.ToString(CultureInfo.InvariantCulture));
                }
                bearings.Add(InitialBearingDeg(lats[i], lons[i], lats[i + 1], lons[i + 1]));
            }
            for (int i = 0; i < bearings.Count - 1; i++)
            {
                double turn = TurnAngleDeg(bearings[i], bearings[i + 1]);
                if (turn > LegacyConfig.MaxTurnDeg)
                {
                    routeValid = false;
                    messages.Add("TURN_EXCEEDED:" + (i + 1).ToString(CultureInfo.InvariantCulture));
                }
            }

            // ── DISTANCE (D1 + D2) ───────────────────────────────────────────────────
            // D1: legacy uses RAW (lon2 - lon1) with NO anti-meridian wrap.
            // D2: round each leg to LegacyLegDecimals (banker's) BEFORE summing.
            List<double> legDistances = new List<double>();
            double totalDistance = 0.0;
            for (int i = 0; i < wpCount - 1; i++)
            {
                double lat1 = lats[i];
                double lon1 = lons[i];
                double lat2 = lats[i + 1];
                double lon2 = lons[i + 1];

                // Equirectangular ("quick range") kernel with dlon entering LINEARLY, so the
                // missing wrap (D1) produces a wildly wrong anti-meridian leg.
                double dlonRad = (lon2 - lon1) * Math.PI / 180.0;           // D1: NO wrap
                double dlatRad = (lat2 - lat1) * Math.PI / 180.0;
                double meanLatRad = ((lat1 + lat2) / 2.0) * Math.PI / 180.0;
                double x = dlonRad * Math.Cos(meanLatRad);
                double d = LegacyConfig.EarthRadiusNm * Math.Sqrt(x * x + dlatRad * dlatRad);

                // D2: Math.Round defaults to MidpointRounding.ToEven (banker's), matching
                // Python's round(). Round BEFORE accumulating.
                d = Math.Round(d, LegacyConfig.LegacyLegDecimals, MidpointRounding.ToEven);
                legDistances.Add(d);
                totalDistance += d; // naive left-to-right accumulation (part of D2 drift)
            }

            // ── TIME-ON-TARGET (D3) ──────────────────────────────────────────────────
            // D3: cast travel time to long via TRUNCATION (not round) AND omit the
            // leap-second adjustment that the correct reference applies.
            long travelSec = (long)(totalDistance / LegacyConfig.NominalSpeedNmPerSec); // truncates
            long estimatedTot = launchEpochSec + travelSec;                              // no leaps
            bool totFeasible =
                Math.Abs(estimatedTot - desiredTotEpochSec) <= LegacyConfig.TotTolSec;

            // ── TASKING (PURELY CATEGORICAL, bug-free, preserved) ────────────────────
            // GO requires routeValid AND NOT (MST on SSN). MST is surface-only.
            bool taskingGoNoGo;
            if (!routeValid)
            {
                taskingGoNoGo = false;
            }
            else if (variant == "MST" && platform == "SSN")
            {
                taskingGoNoGo = false;
            }
            else
            {
                taskingGoNoGo = true;
            }

            // ── MESSAGES (match the reference legacy_model ordering exactly) ─────────
            if (!routeValid) messages.Add("ROUTE_INVALID");
            if (!taskingGoNoGo) messages.Add("TASKING_NO_GO");

            // ── DISTRIBUTE / PUBLISH (inline ADO.NET; guarded, never connects) ───────
            if (LegacyConfig.PublishEnabled)
            {
                // This branch is DEAD in the demo (PublishEnabled=false). It exists to model
                // the inline-SQL-in-a-god-method debt and to give static analysis a real
                // data-access coupling. If anyone flips the flag without a DB, the catch
                // keeps the processor deterministic.
                try
                {
                    using (SqlConnection conn =
                        new SqlConnection(LegacyConfig.PublishConnectionString))
                    {
                        conn.Open();
                        // N+1 anti-pattern mirrored from the stored proc: one insert per leg.
                        using (SqlCommand mcmd = new SqlCommand(
                            "INSERT INTO dbo.Missions (MissionId, Platform, Variant, TotalDistanceNm, RouteValid, TaskingGoNoGo) " +
                            "VALUES (@id, @plat, @var, @dist, @valid, @go)", conn))
                        {
                            mcmd.Parameters.AddWithValue("@id", missionId);
                            mcmd.Parameters.AddWithValue("@plat", platform);
                            mcmd.Parameters.AddWithValue("@var", variant);
                            mcmd.Parameters.AddWithValue("@dist", totalDistance);
                            mcmd.Parameters.AddWithValue("@valid", routeValid);
                            mcmd.Parameters.AddWithValue("@go", taskingGoNoGo);
                            mcmd.ExecuteNonQuery();
                        }
                        for (int i = 0; i < legDistances.Count; i++)
                        {
                            using (SqlCommand wcmd = new SqlCommand(
                                "INSERT INTO dbo.Waypoints (MissionId, Seq, LatDeg, LonDeg, LegDistanceNm) " +
                                "VALUES (@id, @seq, @lat, @lon, @leg)", conn))
                            {
                                wcmd.Parameters.AddWithValue("@id", missionId);
                                wcmd.Parameters.AddWithValue("@seq", i);
                                wcmd.Parameters.AddWithValue("@lat", lats[i]);
                                wcmd.Parameters.AddWithValue("@lon", lons[i]);
                                wcmd.Parameters.AddWithValue("@leg", legDistances[i]);
                                wcmd.ExecuteNonQuery();
                            }
                        }
                    }
                    messages.Add("PUBLISHED");
                }
                catch (Exception)
                {
                    messages.Add("PUBLISH_SKIPPED");
                }
            }

            // ── SERIALIZE RESULT ─────────────────────────────────────────────────────
            return BuildResultJson(missionId, legDistances, totalDistance, routeValid,
                estimatedTot, totFeasible, taskingGoNoGo, messages);
        }

        // ── Shared BUG-FREE helpers (identical to the reference modern path) ─────────

        private static double WrapDlon(double dlon)
        {
            while (dlon > 180.0) dlon -= 360.0;
            while (dlon < -180.0) dlon += 360.0;
            return dlon;
        }

        private static double InitialBearingDeg(double lat1, double lon1, double lat2, double lon2)
        {
            double rlat1 = lat1 * Math.PI / 180.0;
            double rlat2 = lat2 * Math.PI / 180.0;
            double dlon = WrapDlon(lon2 - lon1) * Math.PI / 180.0;
            double y = Math.Sin(dlon) * Math.Cos(rlat2);
            double x = Math.Cos(rlat1) * Math.Sin(rlat2)
                       - Math.Sin(rlat1) * Math.Cos(rlat2) * Math.Cos(dlon);
            double brg = Math.Atan2(y, x) * 180.0 / Math.PI;
            return (brg + 360.0) % 360.0;
        }

        private static double TurnAngleDeg(double b1, double b2)
        {
            double d = Math.Abs(b2 - b1) % 360.0;
            return d > 180.0 ? 360.0 - d : d;
        }

        // ── Manual JSON writer so output key order/format is deterministic and matches
        //    the corpus comparison performed by LegacyCheck. ──────────────────────────
        private static string BuildResultJson(string missionId, List<double> legDistances,
            double totalDistance, bool routeValid, long estimatedTot, bool totFeasible,
            bool taskingGoNoGo, List<string> messages)
        {
            // We emit via System.Text.Json Utf8JsonWriter for correct escaping; LegacyCheck
            // parses both sides and compares field-by-field, so key order is irrelevant to
            // correctness — but we keep it stable for readability.
            var buffer = new System.IO.MemoryStream();
            var opts = new JsonWriterOptions { Indented = false };
            using (var w = new Utf8JsonWriter(buffer, opts))
            {
                w.WriteStartObject();
                w.WriteString("missionId", missionId);
                w.WriteStartArray("legDistancesNm");
                foreach (double d in legDistances) w.WriteNumberValue(d);
                w.WriteEndArray();
                w.WriteNumber("totalDistanceNm", totalDistance);
                w.WriteBoolean("routeValid", routeValid);
                w.WriteNumber("estimatedTotEpochSec", estimatedTot);
                w.WriteBoolean("totFeasible", totFeasible);
                w.WriteBoolean("taskingGoNoGo", taskingGoNoGo);
                w.WriteStartArray("messages");
                foreach (string m in messages) w.WriteStringValue(m);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}
