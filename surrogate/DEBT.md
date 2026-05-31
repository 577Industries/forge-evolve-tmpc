# Synthetic Technical-Debt Catalog (surrogate)

**This document is part of the FORGE EVOLVE for TMPC _synthetic, unclassified_ surrogate.**

Everything described here is **100% synthetic and authored from scratch**. The "plausible
real-MDS analog" column is an *illustrative, in-kind* mapping only — it is **NOT a claim of
fidelity** to any real Theater Mission Planning Center (TMPC), Mission Distribution System
(MDS), TED/TMT, or Tomahawk Weapon System code, data, algorithm, or parameter. The surrogate
was engineered to exhibit the same **classes** of technical debt and the same **shapes** of
mission-planning logic the government described in the topic Q&A (a ~1.3M-LOC, mostly-C# MDS
with SQL and JavaScript and a little VB6), so modernization results transfer **in kind, not in
fidelity**. See [`../EXCLUSIONS.md`](../EXCLUSIONS.md).

## Debt items → plausible real-MDS analog

| # | Synthetic debt item (where) | Class of debt | Plausible real-MDS analog (illustrative, NOT a fidelity claim) | How modernization addresses it |
|---|---|---|---|---|
| 1 | **God class / high CC** — `MissionProcessor.ProcessMission` is one ~200-line method with cyclomatic complexity > 30 (parse + validate + distance + TOT + tasking + publish all inlined) | Maintainability / testability | A long-lived mission-processing routine that accreted parse, validate, route, time, and distribute responsibilities over decades into one procedure | Decompose into single-responsibility units (parse / validate / route / TOT / tasking / publish); target max method CC < 10 (pre-registered) |
| 2 | **Static mutable config** — `LegacyConfig` global static fields mutated at startup, read deep in processing | Global state / thread-safety | A `DistributionConfig`-style singleton holding tunables and connection state, read implicitly throughout the code | Replace with injected, immutable options objects |
| 3 | **Inline data access** — `Microsoft.Data.SqlClient` connection + commands embedded inside the god method (guarded `PublishEnabled=false`, never connects) | Coupling / separation-of-concerns | SQL string-building and ADO.NET calls interleaved with business logic in UI/processing layers | Extract a repository/port behind the contract; parameterized, set-based access |
| 4 | **D1 — anti-meridian distance bug** — legacy leg distance uses **raw** `(lon2 - lon1)` with no wrap, so legs crossing ±180° are wildly wrong (`MissionProcessor.cs`) | Correctness (geo math) | A "quick range" distance helper that fails to normalize longitude across the date line | Modern path wraps `dLon` to `[-180, 180]`; flagged as an **intentional divergence** finding |
| 5 | **D2 — precision drift** — each leg is `Math.Round(d, 8)` (banker's) **before** summing, so error accumulates over many legs (`MissionProcessor.cs`) | Correctness (floating-point) | Intermediate rounding / fixed-precision truncation in accumulation loops, plus FLOAT persistence (item 8) | Modern path sums exactly (no intermediate rounding); emit precision-constrained values with `decimal`/checked arithmetic where the CLAR flags them |
| 6 | **D3 — TOT truncation + omitted leap seconds** — legacy casts travel time via `(long)` truncation (not round) and omits the leap-second adjustment (`MissionProcessor.cs`) | Correctness (time math) | Integer truncation of a time delta and a missing UTC leap-second correction in a time-on-target estimator | Modern path rounds and applies the (synthetic) leap-second table; TOT-feasibility flips are recorded as **intentional divergence** findings |
| 7 | **VB6 fixed-point geo** — `GeoFixedPoint.bas` scaled-integer "mil-grid" lat/lon with `GoTo` error handling, `Variant` typing, and a half-wrapped longitude delta | Legacy language / dead-but-present | A surviving VB6 utility module in the tool-chain doing fixed-point coordinate conversions | VB6 → TypeScript (an **already-validated** FORGE EVOLVE path); the C#/.NET path is the new TMPC extension |
| 8 | **jQuery rule drift** — `wwwroot/mission-review.js` re-implements the tasking rule client-side but the copy is **stale** (wrongly allows MST on SSN) | Duplicated logic / client-server drift | A browser UI that forked a business rule from an older server version and silently drifted out of sync | Single source of truth (server contract); UI consumes it instead of re-deriving |
| 9 | **SQL FLOAT precision loss** — `schema.sql` stores distance/coordinate values in approximate `FLOAT` columns | Data-layer precision | Approximate numeric columns used where exact `DECIMAL` is required, reintroducing drift on every round-trip | Migrate `FLOAT → DECIMAL(…, …)`, add CHECK constraints |
| 10 | **SQL N+1 insert** — `sp_PublishMission.sql` cursors over a delimited blob and INSERTs one waypoint per iteration; no transaction; no FK | Performance / integrity | A publish proc that loops row-by-row instead of set-based, with a delimited-string interface and missing referential integrity | Set-based `INSERT…SELECT` (or TVP), wrap in a transaction, add the FK |

## Defects D1/D2/D3 and the equivalence story

D1, D2, and D3 are the **only** differences between the legacy and the (reference) modern
semantics, and they are deliberately confined to the **continuous** outputs
(`legDistancesNm`, `totalDistanceNm`) and — through travel time — the **time-on-target**
outputs (`estimatedTotEpochSec`, `totFeasible`):

- **`routeValid`** and **`taskingGoNoGo`** are computed by **bug-free, shared** code paths
  (wrapped-`dLon` degree-box, turn-rate, and the purely categorical MST-surface-only rule),
  so they are **preserved exactly** between legacy and reference on 100% of corpus vectors.
- The corpus marks a vector `expectedLegacyDivergent = true` when a continuous distance field
  differs by more than `1e-9` relative **or** the `totFeasible` decision flips. This is a
  **minority** (~16%) of the corpus; the majority are equivalent. These divergences are the
  ground truth the validation harness should reproduce and report as **intentional
  divergences** (`IsIntentionalDivergence = true`), not equivalence failures.

## Honesty note on D2 calibration

The legacy D2 rounding precision is **8 decimal places** of a nautical mile (see
`LEGACY_LEG_DECIMALS` in `reference/reference.py` and `LegacyConfig.LegacyLegDecimals` in
`MissionProcessor.cs`). This is a deliberate surrogate calibration: it makes D2 an
**accumulation-dominated** defect (a single short leg stays within the `1e-9` equivalence
tolerance; a long chain of short legs drifts past it), rather than a per-leg defect that would
trivially make every multi-leg route divergent. The *class* of debt (intermediate rounding
before summation, plus FLOAT persistence) is the realistic part; the exact decimal place is a
tuning knob chosen so the corpus has a meaningful but minority divergent fraction.
