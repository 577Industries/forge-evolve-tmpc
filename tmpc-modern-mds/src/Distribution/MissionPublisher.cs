// Distribution / publish responsibility, extracted from the legacy god method's inline ADO.NET.
//
// SECURITY-HARDENED (output-neutral publish path ONLY — does NOT affect computed mission outputs,
// so corpus equivalence is preserved):
//   * PARAMETERIZED commands via SqlParameter (the legacy already used AddWithValue, but the
//     command text is now a fixed parameterized template with NO string concatenation of values);
//   * NO hardcoded connection string — it is INJECTED via PublishOptions (null by default);
//   * NO TrustServerCertificate=true (removed from the connection string entirely);
//   * still guarded by PublishEnabled=false (the demo default) so it NEVER connects.
//
// This is the async distribution method required by the clean-architecture brief. When publishing
// is disabled it returns immediately with PUBLISH_SKIPPED-free behavior (the legacy added no
// message when disabled, and neither do we), keeping the corpus output byte-identical.

using ForgeEvolve.ModernMds.Models;
using Microsoft.Data.SqlClient;

namespace ForgeEvolve.ModernMds.Distribution;

/// <summary>Publishes a computed mission (output-neutral). Single responsibility.</summary>
public interface IMissionPublisher
{
    /// <summary>
    /// Asynchronously publish the mission. Returns an optional status message to append to the
    /// result's message list (null = append nothing — matching the legacy's disabled-path behavior).
    /// Never throws: failures are swallowed and reported as a "PUBLISH_SKIPPED" message, exactly
    /// like the legacy.
    /// </summary>
    Task<string?> PublishAsync(
        MissionRequest request,
        DistanceResult distance,
        bool routeValid,
        bool taskingGoNoGo,
        MissionOptions options,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class MissionPublisher : IMissionPublisher
{
    public async Task<string?> PublishAsync(
        MissionRequest request,
        DistanceResult distance,
        bool routeValid,
        bool taskingGoNoGo,
        MissionOptions options,
        CancellationToken cancellationToken = default)
    {
        // Output-neutral guard: disabled by default. Append nothing (legacy parity).
        if (!options.Publish.PublishEnabled)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(options.Publish.ConnectionString))
        {
            // No injected connection string => skip safely (never connects, no secret embedded).
            return "PUBLISH_SKIPPED";
        }

        try
        {
            await using var conn = new SqlConnection(options.Publish.ConnectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await InsertMissionAsync(conn, request, distance.TotalDistanceNm, routeValid, taskingGoNoGo, cancellationToken)
                .ConfigureAwait(false);
            await InsertWaypointsAsync(conn, request, distance, cancellationToken).ConfigureAwait(false);
            return "PUBLISHED";
        }
        catch (Exception)
        {
            // Legacy parity: swallow and emit a skipped message; stays deterministic.
            return "PUBLISH_SKIPPED";
        }
    }

    private static async Task InsertMissionAsync(
        SqlConnection conn,
        MissionRequest request,
        double totalDistanceNm,
        bool routeValid,
        bool taskingGoNoGo,
        CancellationToken ct)
    {
        // Fixed parameterized literal (no value concatenation); values flow via SqlParameter only.
        await using SqlCommand cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO dbo.Missions (MissionId, Platform, Variant, TotalDistanceNm, RouteValid, TaskingGoNoGo) " +
            "VALUES (@id, @plat, @var, @dist, @valid, @go)";
        cmd.Parameters.Add(new SqlParameter("@id", request.MissionId));
        cmd.Parameters.Add(new SqlParameter("@plat", request.Platform));
        cmd.Parameters.Add(new SqlParameter("@var", request.Variant));
        cmd.Parameters.Add(new SqlParameter("@dist", totalDistanceNm));
        cmd.Parameters.Add(new SqlParameter("@valid", routeValid));
        cmd.Parameters.Add(new SqlParameter("@go", taskingGoNoGo));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertWaypointsAsync(
        SqlConnection conn,
        MissionRequest request,
        DistanceResult distance,
        CancellationToken ct)
    {
        for (int i = 0; i < distance.LegDistancesNm.Count; i++)
        {
            await using SqlCommand cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO dbo.Waypoints (MissionId, Seq, LatDeg, LonDeg, LegDistanceNm) " +
                "VALUES (@id, @seq, @lat, @lon, @leg)";
            cmd.Parameters.Add(new SqlParameter("@id", request.MissionId));
            cmd.Parameters.Add(new SqlParameter("@seq", i));
            cmd.Parameters.Add(new SqlParameter("@lat", request.Waypoints[i].LatDeg));
            cmd.Parameters.Add(new SqlParameter("@lon", request.Waypoints[i].LonDeg));
            cmd.Parameters.Add(new SqlParameter("@leg", distance.LegDistancesNm[i]));
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }
}
