-- ─────────────────────────────────────────────────────────────────────────────
-- sp_PublishMission.sql — SYNTHETIC, INTENTIONALLY-LEGACY publish stored procedure.
--
-- PART OF: FORGE EVOLVE for TMPC, synthetic unclassified surrogate (Phase 1).
--
-- Models legacy SQL technical debt:
--   * N+1 INSERT pattern: a cursor loops over waypoints and issues one INSERT per row
--     instead of a single set-based INSERT...SELECT — a classic performance/scaling smell.
--   * Writes the precision-losing FLOAT columns from dbo.schema.sql.
--   * No explicit transaction around the parent + child writes (atomicity smell).
--   * Waypoints arrive as a comma/semicolon-delimited string parsed in T-SQL (legacy
--     "pass a blob and split it" interface), rather than a table-valued parameter.
--
-- 100% synthetic and unclassified. Not executed by the demo. Present for discovery and as
-- a modernization target (cursor -> set-based, add a transaction, use a TVP).
-- ─────────────────────────────────────────────────────────────────────────────

IF OBJECT_ID('dbo.sp_PublishMission', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_PublishMission;
GO

CREATE PROCEDURE dbo.sp_PublishMission
    @MissionId       VARCHAR(64),
    @Platform        VARCHAR(8),
    @Variant         VARCHAR(16),
    @LaunchEpochSec  BIGINT,
    @DesiredTotEpoch BIGINT,
    @TotalDistanceNm FLOAT,
    @EstimatedTot    BIGINT,
    @RouteValid      BIT,
    @TotFeasible     BIT,
    @TaskingGoNoGo   BIT,
    -- Waypoints as "lat,lon,leg;lat,lon,leg;..." (legacy delimited-blob interface).
    @WaypointBlob    NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    -- Parent row (FLOAT distance => precision loss on store).
    MERGE dbo.Missions AS tgt
    USING (SELECT @MissionId AS MissionId) AS src
        ON tgt.MissionId = src.MissionId
    WHEN MATCHED THEN UPDATE SET
        Platform = @Platform, Variant = @Variant,
        LaunchEpochSec = @LaunchEpochSec, DesiredTotEpoch = @DesiredTotEpoch,
        TotalDistanceNm = @TotalDistanceNm, EstimatedTot = @EstimatedTot,
        RouteValid = @RouteValid, TotFeasible = @TotFeasible,
        TaskingGoNoGo = @TaskingGoNoGo
    WHEN NOT MATCHED THEN INSERT
        (MissionId, Platform, Variant, LaunchEpochSec, DesiredTotEpoch,
         TotalDistanceNm, EstimatedTot, RouteValid, TotFeasible, TaskingGoNoGo)
        VALUES
        (@MissionId, @Platform, @Variant, @LaunchEpochSec, @DesiredTotEpoch,
         @TotalDistanceNm, @EstimatedTot, @RouteValid, @TotFeasible, @TaskingGoNoGo);

    DELETE FROM dbo.Waypoints WHERE MissionId = @MissionId;

    -- ── N+1 INSERT (the debt) ────────────────────────────────────────────────
    -- Split the delimited blob and INSERT one waypoint at a time inside a CURSOR.
    DECLARE @seq INT = 0;
    DECLARE @row NVARCHAR(200);
    DECLARE @lat FLOAT, @lon FLOAT, @leg FLOAT;
    DECLARE @p1 INT, @p2 INT;

    DECLARE rowCursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT LTRIM(RTRIM(value))
        FROM STRING_SPLIT(@WaypointBlob, ';')
        WHERE LTRIM(RTRIM(value)) <> '';

    OPEN rowCursor;
    FETCH NEXT FROM rowCursor INTO @row;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Parse "lat,lon,leg" (no validation — legacy trust-the-blob smell).
        SET @p1 = CHARINDEX(',', @row);
        SET @p2 = CHARINDEX(',', @row, @p1 + 1);
        SET @lat = TRY_CONVERT(FLOAT, SUBSTRING(@row, 1, @p1 - 1));
        SET @lon = TRY_CONVERT(FLOAT, SUBSTRING(@row, @p1 + 1, @p2 - @p1 - 1));
        SET @leg = TRY_CONVERT(FLOAT, SUBSTRING(@row, @p2 + 1, LEN(@row) - @p2));

        -- One INSERT per waypoint => N+1 round-trips.
        INSERT INTO dbo.Waypoints (MissionId, Seq, LatDeg, LonDeg, LegDistanceNm)
        VALUES (@MissionId, @seq, @lat, @lon, @leg);

        SET @seq = @seq + 1;
        FETCH NEXT FROM rowCursor INTO @row;
    END

    CLOSE rowCursor;
    DEALLOCATE rowCursor;
END
GO
