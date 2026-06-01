// FORGE EVOLVE for TMPC — typed view of a MissionResult JSON document.
//
// The runners exchange JSON strings (the frozen contract seam). The oracles, however, reason
// over NAMED mission outputs, so we parse each side's JSON once into this small typed shape.
// Parsing is tolerant (a malformed/partial result degrades to a sentinel) so a crash in one
// implementation is surfaced as an oracle violation, never as an exception escaping Verify().

using System.Globalization;
using System.Text.Json;

namespace ForgeEvolve.Validation;

/// <summary>
/// A parsed mission result. Mirrors the fields emitted by both the legacy
/// <c>MissionProcessor</c> and the reference model: discrete decisions, continuous distances,
/// the time-on-target estimate, and the message log.
/// </summary>
public sealed class MissionResult
{
    /// <summary>True when the JSON parsed into the expected mission-result shape.</summary>
    public bool Parsed { get; init; }

    // ── Discrete outputs (exact-equality oracles, tolerance 0) ──
    public string MissionId { get; init; } = "";
    public bool RouteValid { get; init; }
    public bool TotFeasible { get; init; }
    public bool TaskingGoNoGo { get; init; }
    public long EstimatedTotEpochSec { get; init; }
    public int WaypointCount { get; init; }     // = legDistancesNm.Length + 1 (or 0 when empty)
    public int MessageCount { get; init; }

    // ── Continuous outputs (bounded-relative-error oracles) ──
    public IReadOnlyList<double> LegDistancesNm { get; init; } = Array.Empty<double>();
    public double TotalDistanceNm { get; init; }

    /// <summary>The raw message log (used only for the message-count discrete oracle).</summary>
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    private static readonly MissionResult Unparsed = new() { Parsed = false };

    /// <summary>Parse a mission-result JSON string. Never throws; returns an unparsed
    /// sentinel on malformed input (so the oracles record a violation, not a crash).</summary>
    public static MissionResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Unparsed;
        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Unparsed;

            var legs = new List<double>();
            if (root.TryGetProperty("legDistancesNm", out var legEl)
                && legEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in legEl.EnumerateArray())
                    legs.Add(e.GetDouble());
            }

            var messages = new List<string>();
            if (root.TryGetProperty("messages", out var msgEl)
                && msgEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in msgEl.EnumerateArray())
                    messages.Add(e.GetString() ?? "");
            }

            // A route with k legs has k+1 waypoints; an empty route has 0.
            int waypointCount = legs.Count == 0 ? 0 : legs.Count + 1;

            return new MissionResult
            {
                Parsed = true,
                MissionId = GetString(root, "missionId"),
                RouteValid = GetBool(root, "routeValid"),
                TotFeasible = GetBool(root, "totFeasible"),
                TaskingGoNoGo = GetBool(root, "taskingGoNoGo"),
                EstimatedTotEpochSec = GetInt64(root, "estimatedTotEpochSec"),
                TotalDistanceNm = GetDouble(root, "totalDistanceNm"),
                LegDistancesNm = legs,
                Messages = messages,
                WaypointCount = waypointCount,
                MessageCount = messages.Count,
            };
        }
        catch (JsonException)
        {
            return Unparsed;
        }
    }

    private static string GetString(JsonElement o, string name)
        => o.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString() ?? "" : "";

    private static bool GetBool(JsonElement o, string name)
        => o.TryGetProperty(name, out var e)
           && (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False)
           && e.GetBoolean();

    private static long GetInt64(JsonElement o, string name)
        => o.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number
           && e.TryGetInt64(out var v) ? v : 0L;

    private static double GetDouble(JsonElement o, string name)
        => o.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number
            ? e.GetDouble() : 0.0;

    /// <summary>Count of waypoints in a MissionRequest input JSON (for input-derived checks).</summary>
    public static int WaypointCountOfInput(string inputJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            if (doc.RootElement.TryGetProperty("waypoints", out var wp)
                && wp.ValueKind == JsonValueKind.Array)
                return wp.GetArrayLength();
        }
        catch (JsonException) { /* fall through */ }
        return 0;
    }

    /// <summary>Invariant-culture round-trip of a double (diagnostics only).</summary>
    public static string Fmt(double d) => d.ToString("R", CultureInfo.InvariantCulture);
}
