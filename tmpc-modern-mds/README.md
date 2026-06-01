# ForgeEvolve.ModernMds — behavior-preserving modernization of the legacy MissionProcessor

**Part of FORGE EVOLVE for TMPC (synthetic, unclassified surrogate — Phase 1).**

This is the **modernized mission component**: a clean .NET 8 re-architecture of the synthetic
legacy `MissionProcessor.ProcessMission`
(`surrogate/tmpc-surrogate-mds/legacy/MissionProcessor.cs`). It is **behaviorally equivalent to the
legacy** — its output JSON **equals the legacy `legacyOutput` for all 2000 corpus vectors** (discrete
fields exact incl. messages; continuous distance fields within `1e-9` relative). The proof is
`tools/ModernCheck`, which prints `MODERN-CHECK PASS: 2000/2000`.

> **Zero silent regression.** Refactoring changed **structure, not behavior**. The legacy
> computational quirks **D1, D2, D3 are reproduced exactly** and are **NOT fixed here**; they are
> surfaced separately as **ECP-recommended findings** (see below), per the government's
> *"preserve behavior; changes via ECP"* guidance.

## Entry point

```csharp
var service = ForgeEvolve.ModernMds.Services.MissionService.CreateDefault();
string outputJson = service.ProcessMission(inputJson); // == legacy output, byte-comparable
```

`MissionService` exposes the synchronous, legacy-equivalent `ProcessMission(string)` plus an async
`ProcessMissionAsync(string, CancellationToken)` (the distribution step is async).

## What changed (structure) — clean architecture

The legacy was **one ~200-line god method with cyclomatic complexity 49** (parse + pre-validate
diagnostics + route-validation + distance + TOT + tasking + inline-SQL publish + serialize, all
inlined, with magic numbers, deep nesting, and a static mutable `LegacyConfig`). It is now decomposed
into single-responsibility, dependency-injected units, each with **cyclomatic complexity < 10**
(measured max = **6**):

| Responsibility | Type (interface) | File |
|---|---|---|
| Parse + default | `IMissionParser` | `src/Parsing/MissionParser.cs` |
| Route validation (degree-box + turn-rate) | `IRouteValidator` | `src/Routing/RouteValidator.cs` |
| Distance (D1 + D2 preserved) | `IDistanceCalculator` | `src/Geometry/DistanceCalculator.cs` |
| Geometry kernels (stateless) | — | `src/Geometry/GeoMath.cs` |
| Time-on-target (D3 preserved) | `ITotEstimator` | `src/Timing/TotEstimator.cs` |
| Tasking GO/NO-GO (categorical) | `ITaskingEvaluator` | `src/Tasking/TaskingEvaluator.cs` |
| Distribution / publish (async, hardened) | `IMissionPublisher` | `src/Distribution/MissionPublisher.cs` |
| Serialize (legacy-compatible JSON) | `IMissionResultSerializer` | `src/Serialization/MissionResultSerializer.cs` |
| Orchestration (replaces the god method) | `MissionService` | `src/Services/MissionService.cs` |
| Composition root (DI wiring) | `MissionServiceFactory` | `src/Services/MissionServiceFactory.cs` |
| Domain records (immutable) | — | `src/Models/MissionModels.cs` |
| Injected options (replaces static config) | — | `src/Models/MissionOptions.cs` |

Cross-cutting modernizations:

- **Immutable `record` models** (`MissionRequest`, `MissionResult`, `Waypoint`, …) replace the
  legacy parallel `List<double>` arrays and loose locals.
- **Dependency injection** via interfaces + a self-contained composition root
  (`MissionServiceFactory`); no global state.
- **Injected, immutable options** (`MissionOptions`) replace the static mutable `LegacyConfig`
  global-state smell.
- **Nullable reference types ON**, `TreatWarningsAsErrors=true` — the project builds with zero
  warnings/errors.
- **Async distribution** (`IMissionPublisher.PublishAsync`).

## Security hardening — output-neutral publish path ONLY

The legacy inline ADO.NET "publish" lived inside the god method, used a **hardcoded connection
string** with `TrustServerCertificate=true`, and was guarded by `PublishEnabled=false` (never
connects in the demo). In `src/Distribution/MissionPublisher.cs` that path is:

- **Parameterized** — fixed SQL command templates with values bound exclusively via `SqlParameter`
  (no string concatenation of values).
- **Free of any hardcoded connection string** — the connection string is **injected** via
  `PublishOptions.ConnectionString` (null by default).
- **Free of `TrustServerCertificate=true`** — removed entirely.
- Still **disabled by default** (`PublishEnabled=false`), so the demo **never opens a connection**.

This path is **output-neutral**: it does not affect any computed mission field, so corpus
equivalence is preserved (`MODERN-CHECK PASS: 2000/2000`). These changes map to STIG/800-53 controls
(SI-10 input validation / parameterized queries, SC-28/IA-5 no embedded secrets, SC-8/SC-13
transport hardening).

## What did NOT change (behavior) — D1/D2/D3 are PRESERVED as ECP findings

The three seeded legacy defects are **deliberately reproduced**, because changing them would break
behavioral equivalence. They are recorded as **ECP-recommended findings** (`IsIntentionalDivergence`
in the validation story), **not fixed in this component**:

| Defect | Legacy behavior (preserved here) | ECP-recommended fix (NOT applied here) |
|---|---|---|
| **D1 — anti-meridian distance** | Equirectangular leg distance uses the **raw, unwrapped** `(lon2 - lon1)`; legs crossing ±180° are wildly wrong. (`GeoMath.LegLegacyDistanceNm`) | Wrap `dLon` to `[-180, 180]` (`GeoMath.LegCorrectDistanceNm` is provided but unused). |
| **D2 — precision drift** | Each leg is `Math.Round(d, 8, ToEven)` **before** summing (naive left-to-right accumulation), so error accumulates over many legs. (`DistanceCalculator`) | Sum exactly with no intermediate rounding (and persist as `DECIMAL`). |
| **D3 — TOT truncation + omitted leap seconds** | Travel time cast to `long` by **truncation** (not round) and the synthetic leap-second adjustment **omitted**. (`TotEstimator`) | Round travel time and apply the (synthetic) leap-second table carried in `MissionOptions`. |

The categorical decisions (`routeValid`, `taskingGoNoGo`) use bug-free shared paths and are preserved
exactly vs. both the legacy and the reference, by construction.

## Proving equivalence

```bash
export PATH="$HOME/.dotnet:$PATH"; export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet run --project tmpc-modern-mds/tools/ModernCheck
# -> MODERN-CHECK PASS: 2000/2000
```

`ModernCheck` (`tools/ModernCheck/Program.cs`) loads `surrogate/corpus/corpus.json`, runs
`MissionService.ProcessMission` on every input, and asserts the modern output equals the stored
`legacyOutput` (discrete exact incl. messages; continuous within `1e-9` relative). It is the
zero-silent-regression gate and must pass 2000/2000.
