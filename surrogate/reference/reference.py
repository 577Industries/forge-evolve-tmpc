"""reference.py — NEUTRAL CORRECT semantics + a faithful LEGACY port.

PART OF: FORGE EVOLVE for TMPC, synthetic unclassified surrogate (Phase 1).

This module is the single SOURCE OF TRUTH for the "Mission-Data Routing, Validation
& Distribution" domain. It is 100% synthetic and unclassified: it models the *shapes*
of mission-planning logic (great-circle routing, anti-meridian handling, time-on-target
feasibility, categorical tasking go/no-go), NOT any real Tomahawk / TMPC / MDS algorithm.

It provides two implementations of the same domain:

  * reference_model(...)  — the CORRECT reference semantics (pure float64).
  * legacy_model(...)     — a faithful Python port of the legacy C# defects D1/D2/D3,
                            so the golden corpus can store the expected *buggy* output
                            as a frozen answer key.

The C# `MissionProcessor.ProcessMission` (legacy/MissionLegacy.csproj) must implement
identical arithmetic to legacy_model(...); LegacyCheck proves that against the corpus.

Design invariant (asserted by the corpus generator):
  routeValid and taskingGoNoGo are computed via BUG-FREE SHARED paths and are therefore
  IDENTICAL between reference_model and legacy_model for every input. The defects affect
  ONLY the continuous outputs (legDistancesNm / totalDistanceNm) and, through travel
  time, the TOT outputs (estimatedTotEpochSec / totFeasible).

No I/O, no randomness, no network. Deterministic.
"""

from __future__ import annotations

import math
from typing import Any, Dict, List

# ─────────────────────────────────────────────────────────────────────────────
# Frozen constants (synthetic; documented in the task contract).
# ─────────────────────────────────────────────────────────────────────────────
EARTH_RADIUS_NM = 3440.065
MAX_LEG_NM = 1500.0
MAX_TURN_DEG = 120.0
NOMINAL_SPEED_NM_PER_SEC = 0.15
TOT_TOL_SEC = 120

# Coarse degree-box feasibility proxy (~<=1500nm near the equator). Shared, bug-free.
MAX_LEG_DLAT_DEG = 22.0
MAX_LEG_DLON_DEG = 22.0

# Weapon max range by variant (nm). Used as a documented business constraint; the
# go/no-go rule below is purely categorical and does not depend on it numerically.
WEAPON_MAX_RANGE_NM: Dict[str, float] = {"BlockIV": 900.0, "BlockV": 1000.0, "MST": 1000.0}

# ─────────────────────────────────────────────────────────────────────────────
# Synthetic leap-second table.
#
# These boundary epochs are FICTIONAL placeholders chosen for the surrogate; they do
# NOT correspond to any real IERS leap-second insertion. The rule modeled is: when an
# interval [launch, tot] spans a boundary, the CORRECT estimator adds +1 second per
# boundary crossed (the accumulated UTC-vs-elapsed offset). The legacy estimator (D3)
# omits this adjustment entirely.
#
# Boundaries are sorted ascending. A boundary B is "crossed" by interval [t0, t1]
# (t0 <= t1) iff t0 < B <= t1.
# ─────────────────────────────────────────────────────────────────────────────
SYNTHETIC_LEAP_BOUNDARIES: List[int] = [
    1_000_000_000,
    1_100_000_000,
    1_200_000_000,
    2_000_000_000,
    4_000_000_000,
]


def _leap_seconds_between(t0: int, t1: int) -> int:
    """Count synthetic leap boundaries strictly after t0 and at/under t1.

    Order-independent: uses min/max so it works whether t0 <= t1 or not.
    """
    lo, hi = (t0, t1) if t0 <= t1 else (t1, t0)
    n = 0
    for b in SYNTHETIC_LEAP_BOUNDARIES:
        if lo < b <= hi:
            n += 1
    return n


def _wrap_dlon(dlon: float) -> float:
    """Normalize a longitude delta to [-180, 180]. Correct anti-meridian handling."""
    while dlon > 180.0:
        dlon -= 360.0
    while dlon < -180.0:
        dlon += 360.0
    return dlon


# ─────────────────────────────────────────────────────────────────────────────
# Shared, BUG-FREE geometry helpers used by BOTH models for the categorical paths.
#
# DISTANCE MODEL NOTE (synthetic): the surrogate uses an equirectangular ("flat-Earth"
# small-angle) great-circle proxy for leg distance:
#     d = R * sqrt( (dlat_rad)^2 + (cos(mean_lat) * dlon_rad)^2 )
# This is the *coarse* distance kernel one finds in legacy "quick range" mission code,
# and it is consistent with the coarse degree-box feasibility proxy used for routeValid.
# It is chosen deliberately because the longitude delta enters LINEARLY, which is exactly
# why a missing anti-meridian wrap (D1) produces a catastrophically wrong leg distance
# (unlike a pure haversine, where sin^2(dlon/2) is 360-degree-periodic and would mask
# the missing wrap). This keeps D1 a *real*, observable defect. It is NOT a claim of
# navigational fidelity to any real system.
# ─────────────────────────────────────────────────────────────────────────────
def _leg_distance_correct(lat1: float, lon1: float, lat2: float, lon2: float) -> float:
    """Correct leg distance (nm) with WRAPPED longitude delta (D1-correct)."""
    dlon = math.radians(_wrap_dlon(lon2 - lon1))
    dlat = math.radians(lat2 - lat1)
    mean_lat = math.radians((lat1 + lat2) / 2.0)
    x = dlon * math.cos(mean_lat)
    return EARTH_RADIUS_NM * math.sqrt(x * x + dlat * dlat)


def _leg_distance_legacy(lat1: float, lon1: float, lat2: float, lon2: float) -> float:
    """LEGACY leg distance — D1: uses RAW (lon2 - lon1), no wrapping.

    Identical to the correct version except the longitude delta is NOT normalized,
    so any leg that crosses +/-180 is computed with a >180-degree dlon and is wildly
    wrong. Non-anti-meridian legs are bit-for-bit identical to the correct version.
    """
    dlon = math.radians(lon2 - lon1)  # D1: NO wrap
    dlat = math.radians(lat2 - lat1)
    mean_lat = math.radians((lat1 + lat2) / 2.0)
    x = dlon * math.cos(mean_lat)
    return EARTH_RADIUS_NM * math.sqrt(x * x + dlat * dlat)


def _initial_bearing_deg(lat1: float, lon1: float, lat2: float, lon2: float) -> float:
    """Initial great-circle bearing (deg, 0..360) from point 1 to point 2.

    Uses a WRAPPED longitude delta — this is part of the bug-free shared turn-angle
    path that feeds routeValid, so it must be identical in both models.
    """
    rlat1 = math.radians(lat1)
    rlat2 = math.radians(lat2)
    dlon = math.radians(_wrap_dlon(lon2 - lon1))
    y = math.sin(dlon) * math.cos(rlat2)
    x = (math.cos(rlat1) * math.sin(rlat2)
         - math.sin(rlat1) * math.cos(rlat2) * math.cos(dlon))
    brg = math.degrees(math.atan2(y, x))
    return (brg + 360.0) % 360.0


def _turn_angle_deg(b1: float, b2: float) -> float:
    """Absolute turn between two bearings, in [0, 180]."""
    d = abs(b2 - b1) % 360.0
    return 360.0 - d if d > 180.0 else d


def _route_valid_and_messages(waypoints: List[Dict[str, float]]) -> (bool, List[str]):
    """SHARED, BUG-FREE per-leg degree-box + turn-rate validation.

    Identical in legacy and modern => routeValid is preserved exactly. Returns
    (routeValid, messages-from-validation).
    """
    msgs: List[str] = []
    valid = True
    bearings: List[float] = []
    for i in range(len(waypoints) - 1):
        a = waypoints[i]
        b = waypoints[i + 1]
        dlat = abs(b["latDeg"] - a["latDeg"])
        dlon = abs(_wrap_dlon(b["lonDeg"] - a["lonDeg"]))
        if dlat > MAX_LEG_DLAT_DEG or dlon > MAX_LEG_DLON_DEG:
            valid = False
            msgs.append(f"LEG_OUT_OF_BOX:{i}")
        bearings.append(_initial_bearing_deg(a["latDeg"], a["lonDeg"], b["latDeg"], b["lonDeg"]))
    for i in range(len(bearings) - 1):
        turn = _turn_angle_deg(bearings[i], bearings[i + 1])
        if turn > MAX_TURN_DEG:
            valid = False
            msgs.append(f"TURN_EXCEEDED:{i + 1}")
    return valid, msgs


def _tasking_go_no_go(route_valid: bool, platform: str, variant: str) -> bool:
    """PURELY CATEGORICAL, bug-free, preserved exactly.

    GO requires routeValid AND NOT (variant == MST AND platform == SSN). MST is
    surface-only, so MST-on-SSN is always NO-GO.
    """
    if not route_valid:
        return False
    if variant == "MST" and platform == "SSN":
        return False
    return True


# D2 rounding precision (decimal places). The legacy code rounds each leg distance to
# this many decimals BEFORE summing (banker's rounding), so error accumulates over many
# legs. Chosen so the defect is ACCUMULATION-DOMINATED: a single short leg stays within
# the 1e-9 relative equivalence tolerance, but a long chain of short legs drifts past it.
# (At 2 decimals the per-leg error would already exceed 1e-9, swamping the corpus; this
# is a deliberate, documented surrogate calibration — see DEBT.md / reference docstring.)
LEGACY_LEG_DECIMALS = 8


def _round_half_even(x: float, ndigits: int = LEGACY_LEG_DECIMALS) -> float:
    """Banker's rounding to ndigits, matching C# Math.Round(x, n) and Python round()."""
    return round(x, ndigits)


# ─────────────────────────────────────────────────────────────────────────────
# Public models.
# ─────────────────────────────────────────────────────────────────────────────
def _common_categorical(req: Dict[str, Any]):
    """Compute the shared bug-free pieces: validation + tasking. Returns
    (route_valid, tasking_go, messages)."""
    waypoints = req["waypoints"]
    route_valid, msgs = _route_valid_and_messages(waypoints)
    tasking = _tasking_go_no_go(route_valid, req["platform"], req["variant"])
    return route_valid, tasking, msgs


def reference_model(req: Dict[str, Any]) -> Dict[str, Any]:
    """CORRECT reference semantics (float64)."""
    waypoints = req["waypoints"]
    route_valid, tasking, msgs = _common_categorical(req)

    # D1-correct distances, exact sum (no intermediate rounding) => D2-correct.
    leg_distances: List[float] = []
    for i in range(len(waypoints) - 1):
        a = waypoints[i]
        b = waypoints[i + 1]
        leg_distances.append(_leg_distance_correct(a["latDeg"], a["lonDeg"], b["latDeg"], b["lonDeg"]))
    total = math.fsum(leg_distances)

    # TOT: round travel time, ADD synthetic leap seconds (D3-correct).
    travel = int(round(total / NOMINAL_SPEED_NM_PER_SEC))
    launch = int(req["launchEpochSec"])
    naive_tot = launch + travel
    leaps = _leap_seconds_between(launch, naive_tot)
    estimated_tot = naive_tot + leaps
    tot_feasible = abs(estimated_tot - int(req["desiredTotEpochSec"])) <= TOT_TOL_SEC

    messages = ["REFERENCE"] + msgs + ([] if route_valid else ["ROUTE_INVALID"])
    if not tasking:
        messages.append("TASKING_NO_GO")

    return {
        "missionId": req["missionId"],
        "legDistancesNm": leg_distances,
        "totalDistanceNm": total,
        "routeValid": route_valid,
        "estimatedTotEpochSec": estimated_tot,
        "totFeasible": tot_feasible,
        "taskingGoNoGo": tasking,
        "messages": messages,
    }


def legacy_model(req: Dict[str, Any]) -> Dict[str, Any]:
    """Faithful port of the legacy C# defects D1/D2/D3.

    Categorical outputs (routeValid, taskingGoNoGo) use the SAME bug-free shared paths
    as reference_model, so they are preserved exactly.
    """
    waypoints = req["waypoints"]
    route_valid, tasking, msgs = _common_categorical(req)

    # D1: raw (unwrapped) longitude delta in the haversine.
    # D2: round each leg to 2 decimals (banker's) BEFORE summing => accumulated drift.
    leg_distances: List[float] = []
    total = 0.0
    for i in range(len(waypoints) - 1):
        a = waypoints[i]
        b = waypoints[i + 1]
        d = _leg_distance_legacy(a["latDeg"], a["lonDeg"], b["latDeg"], b["lonDeg"])
        d = _round_half_even(d)  # D2
        leg_distances.append(d)
        total += d

    # D3: truncate travel time to int (no rounding) AND omit the leap-second adjustment.
    travel = int(total / NOMINAL_SPEED_NM_PER_SEC)  # truncation toward zero
    launch = int(req["launchEpochSec"])
    estimated_tot = launch + travel  # D3: no leap seconds added
    tot_feasible = abs(estimated_tot - int(req["desiredTotEpochSec"])) <= TOT_TOL_SEC

    messages = ["LEGACY"] + msgs + ([] if route_valid else ["ROUTE_INVALID"])
    if not tasking:
        messages.append("TASKING_NO_GO")

    return {
        "missionId": req["missionId"],
        "legDistancesNm": leg_distances,
        "totalDistanceNm": total,
        "routeValid": route_valid,
        "estimatedTotEpochSec": estimated_tot,
        "totFeasible": tot_feasible,
        "taskingGoNoGo": tasking,
        "messages": messages,
    }


# ─────────────────────────────────────────────────────────────────────────────
# Divergence helper used by the corpus generator.
#
# DIVERGENCE DEFINITION (the corpus answer key's `expectedLegacyDivergent`):
# a vector is "divergent" iff the legacy output differs from the reference in an
# OPERATIONALLY MEANINGFUL way, defined as ANY of:
#   * a CONTINUOUS distance field (totalDistanceNm or any legDistancesNm[i]) differs
#     by more than `rel_tol` (1e-9) relative  -> driven by D1 (anti-meridian) and the
#     accumulated D2 (precision drift);
#   * the BOOLEAN decision totFeasible flips                                   -> driven
#     by D3 (truncation + omitted leap seconds) crossing the TOT_TOL_SEC edge;
#   * a CATEGORICAL decision (routeValid / taskingGoNoGo) differs  -> by construction this
#     NEVER happens (shared bug-free paths); checked defensively.
#
# NOTE on estimatedTotEpochSec: the *raw* integer estimate differs on ~half of inputs
# (truncation vs rounding is a 1-second effect, plus omitted leap seconds). A sub-second
# / few-second difference that does NOT flip totFeasible is below operational tolerance
# and is deliberately NOT counted as a divergence here — only the decision flip is. The
# generator records the raw estimatedTot delta separately in the manifest for audit.
# ─────────────────────────────────────────────────────────────────────────────
CONTINUOUS_FIELDS = ("legDistancesNm", "totalDistanceNm")
CATEGORICAL_PRESERVED_FIELDS = ("routeValid", "taskingGoNoGo")


def _rel_diff(a: float, b: float) -> float:
    denom = max(abs(a), abs(b), 1e-12)
    return abs(a - b) / denom


def continuous_max_rel_error(reference: Dict[str, Any], legacy: Dict[str, Any]) -> float:
    """Max relative error across all continuous distance fields (total + per-leg)."""
    m = _rel_diff(reference["totalDistanceNm"], legacy["totalDistanceNm"])
    ra = reference["legDistancesNm"]
    la = legacy["legDistancesNm"]
    if len(ra) != len(la):
        return float("inf")
    for x, y in zip(ra, la):
        m = max(m, _rel_diff(x, y))
    return m


def is_legacy_divergent(reference: Dict[str, Any], legacy: Dict[str, Any],
                        rel_tol: float = 1e-9) -> bool:
    """True iff the legacy output is operationally divergent from the reference.

    See the DIVERGENCE DEFINITION comment above for the exact criteria.
    """
    if continuous_max_rel_error(reference, legacy) > rel_tol:
        return True
    if reference["totFeasible"] != legacy["totFeasible"]:
        return True
    # Categorical decisions must be preserved; check defensively.
    for f in CATEGORICAL_PRESERVED_FIELDS:
        if reference[f] != legacy[f]:
            return True
    return False


if __name__ == "__main__":
    # Tiny smoke test: an anti-meridian leg should diverge on distance; a nominal
    # leg should match.
    demo = {
        "missionId": "DEMO-0001",
        "platform": "DDG",
        "variant": "BlockV",
        "launchEpochSec": 1_050_000_000,
        "desiredTotEpochSec": 1_050_010_000,
        "waypoints": [
            {"latDeg": 5.0, "lonDeg": 179.0},
            {"latDeg": 5.0, "lonDeg": -179.0},
        ],
    }
    ref = reference_model(demo)
    leg = legacy_model(demo)
    print("reference total:", ref["totalDistanceNm"])
    print("legacy    total:", leg["totalDistanceNm"])
    print("divergent:", is_legacy_divergent(ref, leg))
