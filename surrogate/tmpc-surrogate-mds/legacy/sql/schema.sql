-- ─────────────────────────────────────────────────────────────────────────────
-- schema.sql — SYNTHETIC, INTENTIONALLY-LEGACY mission-distribution schema.
--
-- PART OF: FORGE EVOLVE for TMPC, synthetic unclassified surrogate (Phase 1).
--
-- Models legacy SQL technical debt typical of a long-lived MDS-like store:
--   * FLOAT (approximate, precision-losing) columns used for distance/coordinate values
--     where DECIMAL would be correct — this LOSES PRECISION on round-trip, the SQL analog
--     of the C# D2 precision-drift defect.
--   * No foreign key from Waypoints to Missions (referential-integrity smell).
--   * No useful indexing strategy beyond the PKs.
--
-- 100% synthetic and unclassified. No real data. T-SQL (SQL Server) dialect.
-- This script is NOT executed by the demo; it is present for the discovery / SQL-grammar
-- analysis and for the modernization to repair (FLOAT -> DECIMAL, add FK + indexes).
-- ─────────────────────────────────────────────────────────────────────────────

IF OBJECT_ID('dbo.Waypoints', 'U') IS NOT NULL DROP TABLE dbo.Waypoints;
IF OBJECT_ID('dbo.Missions', 'U') IS NOT NULL DROP TABLE dbo.Missions;
GO

CREATE TABLE dbo.Missions
(
    MissionId        VARCHAR(64)   NOT NULL,
    Platform         VARCHAR(8)    NOT NULL,   -- DDG | CG | SSN
    Variant          VARCHAR(16)   NOT NULL,   -- BlockIV | BlockV | MST
    LaunchEpochSec   BIGINT        NOT NULL,
    DesiredTotEpoch  BIGINT        NOT NULL,
    -- DEBT: FLOAT is an approximate numeric type. Storing nautical-mile distances here
    -- loses precision vs DECIMAL(12,6) and reintroduces drift on every read/write. This
    -- mirrors the C# D2 precision defect at the persistence layer.
    TotalDistanceNm  FLOAT         NULL,
    EstimatedTot     BIGINT        NULL,
    RouteValid       BIT           NOT NULL DEFAULT (0),
    TotFeasible      BIT           NOT NULL DEFAULT (0),
    TaskingGoNoGo    BIT           NOT NULL DEFAULT (0),
    CreatedUtc       DATETIME2(3)  NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Missions PRIMARY KEY CLUSTERED (MissionId)
);
GO

CREATE TABLE dbo.Waypoints
(
    MissionId        VARCHAR(64)   NOT NULL,
    Seq              INT           NOT NULL,
    -- DEBT: FLOAT again for lat/lon/leg distance — approximate storage of values that the
    -- application treats as precise. No CHECK constraints on coordinate ranges either.
    LatDeg           FLOAT         NOT NULL,
    LonDeg           FLOAT         NOT NULL,
    LegDistanceNm    FLOAT         NULL,
    -- DEBT: no FOREIGN KEY to dbo.Missions(MissionId); orphan waypoints are possible.
    CONSTRAINT PK_Waypoints PRIMARY KEY CLUSTERED (MissionId, Seq)
);
GO
