# CLAR Language Profile — SQL (T-SQL / SQL Server)

> Part of **FORGE EVOLVE for TMPC**. Defines how a SQL source module (schema + stored
> procedures) maps into the four CLAR layers of [`clar-spec/CLAR.schema.json`](../CLAR.schema.json).
> The surrogate's representative SQL artifacts are
> `surrogate/tmpc-surrogate-mds/legacy/sql/schema.sql` and `.../sql/sp_PublishMission.sql`.

`sourceLanguage`: **`Sql`**

## Layer mapping

| SQL construct | CLAR layer | Node `type` |
|---|---|---|
| stored-proc body (sequence of statements) | controlFlow | `PIPELINE` / `SEQUENCE` |
| `IF … ELSE` | controlFlow | `BRANCH` |
| `CASE` expression | controlFlow | `SWITCH` |
| `WHILE` loop | controlFlow | `WHILE_LOOP` |
| `CURSOR` fetch loop (e.g. the N+1 publish) | controlFlow | `FOR_LOOP` / `DO_UNTIL` |
| `BEGIN TRY … BEGIN CATCH` | controlFlow | `TRY_CATCH` |
| trigger body | controlFlow | `EVENT_HANDLER` |
| set-based pipeline (`INSERT…SELECT`) | controlFlow | `PIPELINE` |
| proc parameter (`@MissionId`, …) | dataFlow | `PARAMETER` |
| `DECLARE`d local | dataFlow | `VARIABLE` |
| literal / constant | dataFlow | `CONSTANT` |
| `RETURN` / `OUTPUT` value | dataFlow | `RETURN_VALUE` |
| arithmetic in `SET`/`SELECT` | dataFlow | `ARITHMETIC` |
| `WHERE`/`HAVING` predicate | dataFlow | `COMPARISON` |
| `CAST` / `CONVERT` / `TRY_CONVERT` | dataFlow | `CAST` |
| `SUM` / `COUNT` / `AVG` | dataFlow | `AGGREGATE` |
| table / table-variable / TVP | dataFlow | `COLLECTION` |
| row / `Type` record | dataFlow | `RECORD` |
| `DECIMAL`/`NUMERIC` column | dataFlow | `FIXED_DECIMAL` |
| `FLOAT`/`REAL` column | dataFlow | `FLOATING_POINT` (+ `precisionConstrained`, see below) |
| `DATETIME2` / epoch `BIGINT` | dataFlow | `DATE_TIME` |
| `STRING_SPLIT` / `SUBSTRING` blob parse | dataFlow | `STRING_OP` |
| `CHECK` / `FOREIGN KEY` / business predicate | businessLogic | `CONSTRAINT` / `VALIDATION` |
| `MERGE` / `INSERT` / `UPDATE` / `DELETE` / `SELECT` | infrastructure | `DB_QUERY` |
| `bcp` / `BULK INSERT` / file table | infrastructure | `FILE_IO` |
| linked-server / `OPENQUERY` | infrastructure | `API_CALL` |
| `sp_configure` / settings table | infrastructure | `CONFIGURATION` |
| `RAISERROR` / error log table | infrastructure | `LOGGING` |

## Precision-constrained mapping (load-bearing)

SQL Server `FLOAT`/`REAL` are **approximate** numeric types: storing a nautical-mile distance or
a coordinate in a `FLOAT` column **loses precision on every read/write round-trip** — this is the
persistence-layer analog of the C# **D2** precision-drift defect (see `schema.sql` comments). CLAR
marks every `FLOAT`-backed coordinate/distance/time value `precisionConstrained` so the target
generator (a) computes in `decimal`/`checked` and (b) **migrates the column `FLOAT → DECIMAL`**.

| SQL source type / use | CLAR `clarType` | `precisionConstrained` | Target generator emits |
|---|---|---|---|
| `FLOAT` column holding **lat/lon** (`LatDeg`,`LonDeg`) | `PrecisionConstrained` | `true` | `DECIMAL(9,6)` column + `decimal` app type |
| `FLOAT` column holding **distance** (`TotalDistanceNm`,`LegDistanceNm`) | `PrecisionConstrained` | `true` | `DECIMAL(12,6)` column + `decimal` app type |
| `TRY_CONVERT(FLOAT, …)` of a coordinate blob field | `PrecisionConstrained` | `true` | `TRY_CONVERT(DECIMAL(…))`, no float hop |
| `FLOAT` used for a genuinely approximate metric | `FloatingPoint` | `false` | `FLOAT`/`double` acceptable |
| `DECIMAL`/`NUMERIC`/`MONEY` exact column | `FixedDecimal` | `true` (preserve) | keep `DECIMAL`, `decimal` app type |
| `BIGINT` epoch / `INT` keys & counters | `Integer` | `false` | native integer |
| `BIT` flag (`RouteValid`,`TaskingGoNoGo`) | `Boolean` | `false` | `bool` |
| `VARCHAR`/`NVARCHAR` | `Text` | `false` | `string` |

## Worked example (`schema.sql` + `sp_PublishMission.sql`)

- `dbo.Waypoints.LatDeg / LonDeg / LegDistanceNm` (`FLOAT`) → dataFlow `FLOATING_POINT`,
  `clarType=PrecisionConstrained`, `precisionConstrained=true`, `sourceType="FLOAT"` →
  modernize to `DECIMAL`.
- `dbo.Missions.TotalDistanceNm` (`FLOAT`) → same; the SQL-side carrier of D2 drift.
- `@TotalDistanceNm FLOAT` proc parameter → dataFlow `PARAMETER`, `PrecisionConstrained`.
- The `CURSOR` N+1 per-waypoint `INSERT` → controlFlow `FOR_LOOP` + infrastructure `DB_QUERY`
  on `dbo.Waypoints` (modernization target: cursor → set-based `INSERT…SELECT` with a TVP).
- `RouteValid`/`TotFeasible`/`TaskingGoNoGo` (`BIT`) → dataFlow, **not** precision-constrained.
- Missing `FOREIGN KEY` (Waypoints→Missions) → businessLogic `CONSTRAINT` (referential-integrity
  rule to be added on modernization).
