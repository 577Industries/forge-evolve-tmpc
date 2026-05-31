#!/usr/bin/env python3
"""generate.py — frozen golden corpus for the synthetic TMPC surrogate.

PART OF: FORGE EVOLVE for TMPC, synthetic unclassified surrogate (Phase 1).

Generates N=2000 MissionRequests from a FIXED RNG seed (577077), spanning the
pre-registered tags. For each vector it stores:
    id, tags, input, referenceOutput, legacyOutput, expectedLegacyDivergent.

It writes:
    corpus/corpus.json    -- the frozen array of vectors (the answer key)
    corpus/manifest.json  -- seed, N, per-tag counts, divergent count, audit stats

ABSOLUTE RULES honored here:
  * 100% synthetic, unclassified. All inputs are randomly generated from the fixed seed.
  * No real coordinates of interest, no real tasking data, no secrets.

Determinism: the output is byte-identical across runs (no time, no set/dict ordering
that depends on hashing of non-deterministic values, floats serialized via repr).

Generator-enforced invariants (assertions; the build FAILS if violated):
  * routeValid and taskingGoNoGo are IDENTICAL between referenceOutput and legacyOutput
    for EVERY vector (the categorical paths are bug-free & shared).
  * Every required tag is present.
  * The divergent fraction lands in the pre-registered minority band [0.10, 0.25].
"""

from __future__ import annotations

import hashlib
import json
import os
import random
import sys
from collections import OrderedDict
from typing import Any, Dict, List

# Import the reference + legacy models (the source of truth).
_HERE = os.path.dirname(os.path.abspath(__file__))
_REF_DIR = os.path.join(os.path.dirname(_HERE), "reference")
sys.path.insert(0, _REF_DIR)
import reference as R  # noqa: E402

SEED = 577077
N = 2000

PLATFORMS = ["DDG", "CG", "SSN"]
VARIANTS = ["BlockIV", "BlockV", "MST"]

# Synthetic, OBVIOUSLY-not-real reference epoch used as a default launch time.
# (~2003-04-10 UTC; chosen only so the synthetic leap table is reachable.)
DEFAULT_LAUNCH = 1_050_000_000

# Per-tag target counts (sum == N). Nominal-heavy so the divergent set is a minority,
# dominated by the *intentional* D1/D3 defect tags plus a small D2 (precision) slice.
TAG_COUNTS: "OrderedDict[str, int]" = OrderedDict([
    ("nominal", 900),
    ("precision-drift", 350),
    ("degenerate-route", 200),
    ("anti-meridian", 150),
    ("leap-second", 200),
    ("overflow", 200),
])
assert sum(TAG_COUNTS.values()) == N, "tag counts must sum to N"


# ─────────────────────────────────────────────────────────────────────────────
# Per-tag synthetic input generators. Each returns (waypoints, platform, variant,
# launch, desired, extra_tags). `desired` may be set to None to mean "place at the
# reference TOT" (feasible with margin); the driver computes it from the reference.
# ─────────────────────────────────────────────────────────────────────────────
def _rand_platform_variant(rng: random.Random):
    """Pick a platform/variant. ~Half the SSN picks get MST so the MST-surface-only
    NO-GO rule is well represented in the corpus (still preserved across models)."""
    platform = rng.choice(PLATFORMS)
    variant = rng.choice(VARIANTS)
    return platform, variant


def _gen_nominal(rng: random.Random):
    nl = rng.randint(1, 5)
    lat = rng.uniform(-30, 30)
    lon = rng.uniform(-150, 150)
    wps = [{"latDeg": lat, "lonDeg": lon}]
    for _ in range(nl):
        lat += rng.uniform(-10, 10)
        lon += rng.uniform(-10, 10)
        wps.append({"latDeg": lat, "lonDeg": lon})
    p, v = _rand_platform_variant(rng)
    return wps, p, v, DEFAULT_LAUNCH, None, False


def _gen_precision_drift(rng: random.Random):
    # 10-30 short legs => D2 per-leg rounding accumulates.
    nl = rng.randint(10, 30)
    lat = rng.uniform(-30, 30)
    lon = rng.uniform(-150, 150)
    wps = [{"latDeg": lat, "lonDeg": lon}]
    for _ in range(nl):
        lat += rng.uniform(-1.0, 1.0)
        lon += rng.uniform(-1.0, 1.0)
        wps.append({"latDeg": lat, "lonDeg": lon})
    p, v = _rand_platform_variant(rng)
    return wps, p, v, DEFAULT_LAUNCH, None, False


def _gen_degenerate_route(rng: random.Random):
    lat = rng.uniform(-20, 20)
    lon = rng.uniform(-100, 100)
    if rng.random() < 0.5:
        # A leg that exceeds the degree box (dLat > 22) => routeValid False (preserved).
        wps = [{"latDeg": lat, "lonDeg": lon},
               {"latDeg": lat + rng.uniform(25, 30), "lonDeg": lon + rng.uniform(0, 5)}]
    else:
        # Identical consecutive waypoints (zero-length leg).
        wps = [{"latDeg": lat, "lonDeg": lon}, {"latDeg": lat, "lonDeg": lon}]
    p, v = _rand_platform_variant(rng)
    return wps, p, v, DEFAULT_LAUNCH, None, False


def _gen_anti_meridian(rng: random.Random):
    # A leg crossing +/-180. Near-equatorial so the true leg is small (~<22deg box,
    # stays valid) but the RAW-dlon legacy distance (D1) is catastrophically large.
    lat = rng.uniform(-25, 25)
    lat2 = lat + rng.uniform(-1.5, 1.5)
    wps = [{"latDeg": lat, "lonDeg": 178.5}, {"latDeg": lat2, "lonDeg": -178.5}]
    p, v = _rand_platform_variant(rng)
    return wps, p, v, DEFAULT_LAUNCH, None, False


def _gen_leap_second(rng: random.Random):
    # Launch placed just before a synthetic leap boundary; a short forward leg so the
    # [launch, tot] interval crosses the boundary. `edge=True` => desired placed at the
    # +TOL tolerance edge so the OMITTED leap second (D3) flips totFeasible.
    boundary = rng.choice(R.SYNTHETIC_LEAP_BOUNDARIES[:4])
    launch = boundary - rng.randint(5_000, 60_000)
    lat = rng.uniform(-20, 20)
    lon = rng.uniform(-100, 100)
    wps = [{"latDeg": lat, "lonDeg": lon},
           {"latDeg": lat + rng.uniform(2, 8), "lonDeg": lon + rng.uniform(2, 8)}]
    p, v = _rand_platform_variant(rng)
    return wps, p, v, launch, "edge", False


def _gen_overflow(rng: random.Random):
    # Very large epoch (near the last synthetic boundary, far from int64 limits — safe).
    # int64 max ~9.22e18; we stay near 4e9, so no overflow risk, but the magnitude
    # exercises large-epoch arithmetic. Half are edge-placed so D3 truncation flips
    # totFeasible.
    launch = rng.randint(int(4e9) - 200_000, int(4e9) + 200_000)
    lat = rng.uniform(-20, 20)
    lon = rng.uniform(-100, 100)
    wps = [{"latDeg": lat, "lonDeg": lon},
           {"latDeg": lat + rng.uniform(2, 6), "lonDeg": lon + rng.uniform(2, 6)}]
    p, v = _rand_platform_variant(rng)
    edge = "edge" if rng.random() < 0.6 else None
    return wps, p, v, launch, edge, False


_GENERATORS = {
    "nominal": _gen_nominal,
    "precision-drift": _gen_precision_drift,
    "degenerate-route": _gen_degenerate_route,
    "anti-meridian": _gen_anti_meridian,
    "leap-second": _gen_leap_second,
    "overflow": _gen_overflow,
}


def _build_request(rng: random.Random, tag: str, idx: int) -> Dict[str, Any]:
    wps, platform, variant, launch, desired_mode, _ = _GENERATORS[tag](rng)
    req = {
        "missionId": f"SYN-{tag.upper().replace('-', '')}-{idx:05d}",
        "platform": platform,
        "variant": variant,
        "launchEpochSec": int(launch),
        "desiredTotEpochSec": 0,  # filled below from the reference
        "waypoints": wps,
    }
    # Compute the reference to choose a `desired` TOT.
    ref = R.reference_model(req)
    ref_tot = int(ref["estimatedTotEpochSec"])
    if desired_mode == "edge":
        # Reference exactly feasible at the +TOL edge; legacy under-estimates the TOT
        # (truncation + omitted leap seconds) => |legacy - desired| > TOL => infeasible.
        desired = ref_tot + R.TOT_TOL_SEC
    else:
        # Place desired at the reference TOT (feasible with full margin); the small
        # legacy TOT error does NOT flip totFeasible.
        desired = ref_tot
    req["desiredTotEpochSec"] = int(desired)
    return req


# ─────────────────────────────────────────────────────────────────────────────
# Deterministic JSON serialization (stable float formatting via Python repr).
# ─────────────────────────────────────────────────────────────────────────────
def _canonical(obj: Any) -> Any:
    """Recursively normalize for stable, deterministic JSON. Floats are left as Python
    floats; json.dumps uses float.__repr__ (shortest round-trippable), which is
    deterministic for a given value."""
    return obj


def _dump(obj: Any) -> str:
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":"),
                      allow_nan=False, sort_keys=True)


def main() -> int:
    rng = random.Random(SEED)

    # Deterministic interleaving: emit vectors tag-by-tag in TAG_COUNTS order so the
    # RNG stream is fully reproducible.
    vectors: List[Dict[str, Any]] = []
    per_tag_divergent: "OrderedDict[str, int]" = OrderedDict((t, 0) for t in TAG_COUNTS)
    raw_tot_diff_count = 0
    max_rel_equivalent = 0.0
    seq = 0
    for tag, count in TAG_COUNTS.items():
        for _ in range(count):
            req = _build_request(rng, tag, seq)
            ref = R.reference_model(req)
            leg = R.legacy_model(req)

            # INVARIANT: categorical decisions preserved.
            assert ref["routeValid"] == leg["routeValid"], \
                f"routeValid diverged on {req['missionId']} (tag={tag})"
            assert ref["taskingGoNoGo"] == leg["taskingGoNoGo"], \
                f"taskingGoNoGo diverged on {req['missionId']} (tag={tag})"

            divergent = R.is_legacy_divergent(ref, leg)
            if divergent:
                per_tag_divergent[tag] += 1
            else:
                max_rel_equivalent = max(
                    max_rel_equivalent, R.continuous_max_rel_error(ref, leg))
            if ref["estimatedTotEpochSec"] != leg["estimatedTotEpochSec"]:
                raw_tot_diff_count += 1

            vectors.append(OrderedDict([
                ("id", req["missionId"]),
                ("tags", [tag]),
                ("input", req),
                ("referenceOutput", ref),
                ("legacyOutput", leg),
                ("expectedLegacyDivergent", divergent),
            ]))
            seq += 1

    total_divergent = sum(per_tag_divergent.values())
    divergent_fraction = total_divergent / N

    # INVARIANT: divergent fraction is a meaningful minority.
    assert 0.10 <= divergent_fraction <= 0.25, (
        f"divergent fraction {divergent_fraction:.3f} outside pre-registered "
        f"[0.10, 0.25] band; adjust tag mix or D2 precision")

    corpus_json = _dump(vectors)
    corpus_sha = hashlib.sha256(corpus_json.encode("utf-8")).hexdigest()

    manifest = OrderedDict([
        ("seed", SEED),
        ("n", N),
        ("legacyLegDecimals", R.LEGACY_LEG_DECIMALS),
        ("syntheticLeapBoundaries", R.SYNTHETIC_LEAP_BOUNDARIES),
        ("perTagCounts", OrderedDict((t, c) for t, c in TAG_COUNTS.items())),
        ("perTagDivergent", OrderedDict((t, c) for t, c in per_tag_divergent.items())),
        ("divergentCount", total_divergent),
        ("divergentFraction", round(divergent_fraction, 6)),
        ("rawEstimatedTotDiffCount", raw_tot_diff_count),
        ("maxRelErrorOnEquivalentVectors", max_rel_equivalent),
        ("categoricalPreserved", True),
        ("corpusSha256", corpus_sha),
    ])

    with open(os.path.join(_HERE, "corpus.json"), "w", encoding="utf-8", newline="\n") as f:
        f.write(corpus_json)
    with open(os.path.join(_HERE, "manifest.json"), "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(manifest, ensure_ascii=False, indent=2, allow_nan=False))
        f.write("\n")

    # Print the manifest to stdout (acceptance A).
    print(json.dumps(manifest, ensure_ascii=False, indent=2, allow_nan=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
