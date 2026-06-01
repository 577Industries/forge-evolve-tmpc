// Parse responsibility, extracted from the legacy god method's inlined parse block.
//
// BEHAVIOR-PRESERVING: mirrors the legacy "defensive, swallow-and-emit-a-message" parse exactly:
//   * missing fields default (missionId/platform/variant => "", epochs => 0, waypoints => empty);
//   * any parse exception => a PARSE_ERROR outcome carrying the missionId parsed so far (the legacy
//     starts missionId="" and only assigns it before the throw point if it appeared first).
// The legacy parses missionId first inside the try, so on a malformed-but-present-missionId input
// the partially-parsed id may survive; to be byte-faithful we capture id incrementally and return
// whatever was parsed before the failure, exactly like the legacy local-variable behavior.

using System.Text.Json;
using ForgeEvolve.ModernMds.Models;

namespace ForgeEvolve.ModernMds.Parsing;

/// <summary>Discriminated outcome of a parse: either a request or a parse error.</summary>
public abstract record ParseOutcome
{
    public sealed record Ok(MissionRequest Request) : ParseOutcome;

    /// <summary>Parse failed. <paramref name="MissionId"/> is whatever was parsed before the fault.</summary>
    /// <summary>
    /// Parse failed. <paramref name="MissionId"/> and <paramref name="LaunchEpochSec"/> are whatever
    /// was parsed before the fault (legacy carries the partial launch epoch into estimatedTot).
    /// </summary>
    public sealed record Error(string MissionId, long LaunchEpochSec) : ParseOutcome;
}

/// <summary>Parses a MissionRequest JSON string. Single responsibility: deserialize + default.</summary>
public interface IMissionParser
{
    ParseOutcome Parse(string inputJson);
}

/// <inheritdoc />
public sealed class MissionParser : IMissionParser
{
    public ParseOutcome Parse(string inputJson)
    {
        // Track id and launch epoch incrementally so the parse-error path can carry the partial
        // values, exactly like the legacy local variables (estimatedTot = launchEpochSec on error).
        string missionId = string.Empty;
        long launch = 0L;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(inputJson);
            JsonElement root = doc.RootElement;

            missionId = ReadString(root, "missionId");
            string platform = ReadString(root, "platform");
            string variant = ReadString(root, "variant");
            launch = ReadInt64(root, "launchEpochSec");
            long desiredTot = ReadInt64(root, "desiredTotEpochSec");
            IReadOnlyList<Waypoint> waypoints = ReadWaypoints(root);

            return new ParseOutcome.Ok(new MissionRequest
            {
                MissionId = missionId,
                Platform = platform,
                Variant = variant,
                LaunchEpochSec = launch,
                DesiredTotEpochSec = desiredTot,
                Waypoints = waypoints,
            });
        }
        catch (Exception)
        {
            // Legacy "swallow and emit a message" pattern. Preserve the values parsed so far.
            return new ParseOutcome.Error(missionId, launch);
        }
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement el) ? el.GetString() ?? string.Empty : string.Empty;

    private static long ReadInt64(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement el) ? el.GetInt64() : 0L;

    private static IReadOnlyList<Waypoint> ReadWaypoints(JsonElement root)
    {
        var waypoints = new List<Waypoint>();
        if (root.TryGetProperty("waypoints", out JsonElement wpEl)
            && wpEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement wp in wpEl.EnumerateArray())
            {
                double lat = wp.TryGetProperty("latDeg", out JsonElement laEl) ? laEl.GetDouble() : 0.0;
                double lon = wp.TryGetProperty("lonDeg", out JsonElement loEl) ? loEl.GetDouble() : 0.0;
                waypoints.Add(new Waypoint(lat, lon));
            }
        }
        return waypoints;
    }
}
