# tmpc-surrogate-mds — synthetic, unclassified MDS-like surrogate

Part of **FORGE EVOLVE for TMPC** (U.S. Navy SBIR DON26BZ01-NV013). This directory is a
**100% synthetic, unclassified** stand-in for a Mission-Distribution-System-like component.
It exists so reviewers can run the modernization pipeline end-to-end **without any controlled
data**, against code that exhibits the same *classes* of technical debt and *shapes* of
mission-planning logic the government described.

> **Synthetic / unclassified disclaimer.** Nothing here is real TMPC/MDS/TED/TMT/Tomahawk
> code, data, coordinates, or algorithms. All inputs are randomly generated from a fixed seed.
> The mapping in [`../DEBT.md`](../DEBT.md) is illustrative and **not a claim of fidelity** to
> any real system. See [`../../EXCLUSIONS.md`](../../EXCLUSIONS.md).

## Domain: "Mission-Data Routing, Validation & Distribution"

A `MissionRequest` (platform, variant, launch/desired-TOT epochs, ≥2 waypoints) is processed
into a `MissionResult` (per-leg + total great-circle distance, route validity, estimated TOT,
TOT feasibility, tasking GO/NO-GO, messages). The exact contract and constants are documented
in [`../reference/reference.py`](../reference/reference.py).

## The legacy / modern / reference design

There are **three** implementations of the same domain, by design:

| Role | Where | Purpose |
|---|---|---|
| **Reference (correct)** | [`../reference/reference.py`](../reference/reference.py) `reference_model(...)` | The neutral, correct semantics (float64). Source of truth for the *right* answer. |
| **Legacy (defective)** | `legacy/MissionProcessor.cs` `MissionProcessor.ProcessMission(...)`, and the faithful Python port `reference.py` `legacy_model(...)` | The intentionally-buggy legacy behavior with seeded defects D1/D2/D3. The C# and Python legacy implement **identical arithmetic**. |
| **Modern (to be produced)** | emitted by the pipeline into `tmpc-modern-mds/` | The AI-modernized, secure-by-construction .NET 8 component, validated for behavioral equivalence against the reference. |

**Seeded defects (legacy only):**
- **D1 — anti-meridian:** raw (unwrapped) longitude delta → wrong distance across ±180°.
- **D2 — precision drift:** each leg rounded (banker's) before summing → accumulated error.
- **D3 — TOT:** travel time truncated (not rounded) and the leap-second adjustment omitted.

By construction the defects touch **only** the continuous outputs (distances) and the
time-on-target outputs; the categorical decisions (`routeValid`, `taskingGoNoGo`) use bug-free
shared paths and are **preserved exactly**.

## The corpus is the frozen answer key

[`../corpus/generate.py`](../corpus/generate.py) generates **N = 2000** vectors from a **fixed
seed (577077)** spanning the pre-registered tags (`nominal`, `anti-meridian`, `leap-second`,
`overflow`, `degenerate-route`, `precision-drift`). Each vector stores the input, the
`referenceOutput` (correct), the `legacyOutput` (buggy), and `expectedLegacyDivergent`. The
output is byte-identical across runs (verifiable by SHA-256). `../corpus/manifest.json` records
the seed, counts, and the divergent fraction.

`tools/LegacyCheck` replays the corpus through the **C# legacy** `MissionProcessor` and asserts
it matches the stored Python `legacyOutput` (discrete fields exact; continuous within `1e-9`
relative) — proving the C# legacy faithfully implements the specified defects.

## Build & verify

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# 1. (Re)generate the frozen corpus (deterministic).
python3 ../corpus/generate.py

# 2. Build the legacy library and the check tool.
dotnet build legacy/MissionLegacy.csproj
dotnet build tools/LegacyCheck/LegacyCheck.csproj

# 3. Prove the C# legacy matches the Python answer key.
dotnet run --project tools/LegacyCheck      # -> LEGACY-CHECK PASS: 2000/2000
```

## Layout

```
surrogate/
  reference/reference.py            correct semantics + faithful legacy port
  corpus/generate.py                fixed-seed corpus generator
  corpus/corpus.json                frozen golden vectors (answer key)
  corpus/manifest.json              seed, counts, divergent fraction
  gold/business-rules.gold.ttl      hand-labeled rules for the discovery F1 metric
  DEBT.md                           synthetic-debt → plausible-analog catalog
  tmpc-surrogate-mds/
    legacy/MissionProcessor.cs      god class with seeded defects D1/D2/D3
    legacy/MissionLegacy.csproj     net8.0, Nullable disabled (deliberately legacy)
    legacy/GeoFixedPoint.bas        VB6 fixed-point geo (discovery / VB6 demo)
    legacy/wwwroot/mission-review.js  jQuery UI with a drifted (stale) tasking rule
    legacy/sql/schema.sql           FLOAT-precision schema
    legacy/sql/sp_PublishMission.sql  N+1 insert publish proc
    tools/LegacyCheck/              net8.0 console: corpus replay + assertion
    README.md                       this file
```
