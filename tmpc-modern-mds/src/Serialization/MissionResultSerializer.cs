// Serialization responsibility, extracted from the legacy god method's BuildResultJson.
//
// BEHAVIOR-PRESERVING: writes the same fields in the same order with the same Utf8JsonWriter
// formatting the legacy used, so the emitted JSON is directly comparable to the corpus
// `legacyOutput` (ModernCheck parses both sides and compares field-by-field).

using System.Text;
using System.Text.Json;
using ForgeEvolve.ModernMds.Models;

namespace ForgeEvolve.ModernMds.Serialization;

/// <summary>Serializes a <see cref="MissionResult"/> to the legacy-compatible JSON shape.</summary>
public interface IMissionResultSerializer
{
    string Serialize(MissionResult result);
}

/// <inheritdoc />
public sealed class MissionResultSerializer : IMissionResultSerializer
{
    public string Serialize(MissionResult result)
    {
        using var buffer = new MemoryStream();
        var opts = new JsonWriterOptions { Indented = false };
        using (var w = new Utf8JsonWriter(buffer, opts))
        {
            w.WriteStartObject();
            w.WriteString("missionId", result.MissionId);
            w.WriteStartArray("legDistancesNm");
            foreach (double d in result.LegDistancesNm) w.WriteNumberValue(d);
            w.WriteEndArray();
            w.WriteNumber("totalDistanceNm", result.TotalDistanceNm);
            w.WriteBoolean("routeValid", result.RouteValid);
            w.WriteNumber("estimatedTotEpochSec", result.EstimatedTotEpochSec);
            w.WriteBoolean("totFeasible", result.TotFeasible);
            w.WriteBoolean("taskingGoNoGo", result.TaskingGoNoGo);
            w.WriteStartArray("messages");
            foreach (string m in result.Messages) w.WriteStringValue(m);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
