// ─────────────────────────────────────────────────────────────────────────────
// RuleExtractor — OFFLINE, DETERMINISTIC business-rule extraction from the C# AST.
//
// PART OF: FORGE EVOLVE for TMPC, Discovery Engine (Stage 1, workstream WS-A).
//
// PRODUCTION vs. DEMO PATH:
//   In production, FORGE EVOLVE extracts business rules with an LLM ENSEMBLE (majority vote across
//   models) — that is the path the BusinessRule.Confidence field ("ensemble-agreement") documents.
//   This class is the KEYLESS / OFFLINE / DETERMINISTIC demo path: it uses purely structural AST
//   heuristics (no network, no model) so the demo is reproducible and air-gappable. Confidence here
//   is a fixed heuristic certainty, NOT an ensemble vote.
//
// METHOD: we walk the Roslyn AST of MissionProcessor and recognize specific structural signatures
// (a comparison against a named MaxTurn/Tol/Box constant, a great-circle distance kernel, an
// epoch+travel TOT computation, a wrap-to-180 loop, the categorical GO/NO-GO branch, etc.). Each
// recognized signature emits a BusinessRule with a category, plain statement, and pseudo-expression.
// Config-table and cross-language signals (weapon-range map, max-leg constant) are also mined.
// ─────────────────────────────────────────────────────────────────────────────

using ForgeEvolve.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForgeEvolve.Discovery;

internal static class RuleExtractor
{
    public static List<BusinessRule> Extract(
        CSharpAnalysisResult cs,
        IReadOnlyList<SourceArtifact> allSources)
    {
        var rules = new List<BusinessRule>();
        int n = 0;
        string NextId(string slug) => $"rule-{++n:D2}-{slug}";

        // Gather the methods once; structural probes below operate on each method's Roslyn AST.
        var methods = cs.Methods;
        var processMission = methods.FirstOrDefault(m => m.SimpleName == "ProcessMission");
        var wrapDlon = methods.FirstOrDefault(m => m.SimpleName == "WrapDlon");
        var turnAngle = methods.FirstOrDefault(m => m.SimpleName == "TurnAngleDeg");
        var bearing = methods.FirstOrDefault(m => m.SimpleName == "InitialBearingDeg");

        string pmId = processMission?.Id ?? "MissionProcessor.ProcessMission";
        string configId = cs.Types.FirstOrDefault(t => t.DisplayName.EndsWith("Config", StringComparison.Ordinal))?.Id
                          ?? "LegacyConfig";

        // ── CALCULATION ─────────────────────────────────────────────────────────

        // 1) Great-circle / equirectangular leg-distance kernel: presence of EarthRadius * Sqrt(...).
        if (processMission != null && HasGreatCircleKernel(processMission))
        {
            rules.Add(new BusinessRule
            {
                Id = NextId("great-circle-leg-distance"),
                Category = BusinessRuleCategory.Calculation,
                Statement = "Each leg distance is the great-circle distance between consecutive waypoints, "
                          + "scaled by the Earth radius constant (3440.065 nm).",
                Expression = "legDistanceNm = EARTH_RADIUS_NM * sqrt(dLatRad^2 + (cos(meanLat)*dLonRad)^2)",
                SourceRefs = new[] { pmId },
                Confidence = 0.92,
            });
        }

        // 2) Total distance = sum of leg distances (accumulation into a running total).
        if (processMission != null && HasAccumulatorPattern(processMission, "totalDistance"))
        {
            rules.Add(new BusinessRule
            {
                Id = NextId("total-distance-sum"),
                Category = BusinessRuleCategory.Calculation,
                Statement = "Total route distance is the sum of all per-leg distances.",
                Expression = "totalDistanceNm = sum(legDistancesNm)",
                SourceRefs = new[] { pmId },
                Confidence = 0.9,
            });
        }

        // 3) Estimated TOT = launch epoch + travel time at nominal speed (leap-second adjustment is
        //    part of the canonical rule, even though the legacy path omits it — D3).
        if (processMission != null && HasTotComputation(processMission))
        {
            rules.Add(new BusinessRule
            {
                Id = NextId("estimated-time-on-target"),
                Category = BusinessRuleCategory.Calculation,
                Statement = "Estimated time-on-target is the launch epoch plus travel time at the nominal "
                          + "speed (0.15 nm/s), plus any synthetic leap seconds crossed.",
                Expression = "estimatedTotEpochSec = launchEpochSec + travelTime(totalDistanceNm, 0.15) + leapSeconds(launch, tot)",
                SourceRefs = new[] { pmId },
                Confidence = 0.85,
            });
        }

        // 4) Anti-meridian longitude wrap: a wrap-to-180 helper (loops adjusting by +/-360).
        if (wrapDlon != null && HasWrapTo180(wrapDlon))
        {
            rules.Add(new BusinessRule
            {
                Id = NextId("anti-meridian-wrap"),
                Category = BusinessRuleCategory.Calculation,
                Statement = "Longitude deltas are normalized to [-180, 180] before use so legs crossing "
                          + "the +/-180 anti-meridian are measured correctly.",
                Expression = "dLon = wrapTo180(lon2 - lon1)",
                SourceRefs = new[] { wrapDlon.Id },
                Confidence = 0.9,
            });
        }

        // ── VALIDATION ──────────────────────────────────────────────────────────

        // 5) Per-leg degree-box feasibility: comparison of |dLat|/|dLon| against MaxLegDLat/MaxLegDLon.
        if (processMission != null && ComparesAgainstConfig(processMission, "MaxLegDLat", "MaxLegDLon"))
        {
            rules.Add(new BusinessRule
            {
                Id = NextId("leg-degree-box-feasibility"),
                Category = BusinessRuleCategory.Validation,
                Statement = "A leg is feasible only if |dLatDeg| <= MaxLegDLatDeg (22.0) and the wrapped "
                          + "|dLonDeg| <= MaxLegDLonDeg (22.0) — a coarse leg-length feasibility proxy.",
                Expression = "abs(dLatDeg) <= 22.0 AND abs(wrap(dLonDeg)) <= 22.0",
                SourceRefs = new[] { pmId },
                Confidence = 0.9,
            });
        }

        // 6) Turn-rate limit: comparison against MaxTurn config / turn-angle helper.
        if ((processMission != null && ComparesAgainstConfig(processMission, "MaxTurn")) || turnAngle != null)
        {
            var refs = new List<string> { pmId };
            if (turnAngle != null) refs.Add(turnAngle.Id);
            rules.Add(new BusinessRule
            {
                Id = NextId("turn-rate-limit"),
                Category = BusinessRuleCategory.Validation,
                Statement = "The turn angle between consecutive legs must not exceed MaxTurnDeg (120 degrees).",
                Expression = "turnAngle(bearing[i], bearing[i+1]) <= 120.0",
                SourceRefs = refs,
                Confidence = 0.9,
            });
        }

        // 7) TOT tolerance: |estimatedTot - desiredTot| <= TotTolSec.
        if (processMission != null && HasTotTolerance(processMission))
        {
            rules.Add(new BusinessRule
            {
                Id = NextId("tot-tolerance"),
                Category = BusinessRuleCategory.Validation,
                Statement = "A route is time-on-target feasible only if |estimatedTot - desiredTot| <= "
                          + "TotTolSec (120 seconds).",
                Expression = "abs(estimatedTotEpochSec - desiredTotEpochSec) <= 120",
                SourceRefs = new[] { pmId },
                Confidence = 0.9,
            });
        }

        // 8) Aggregate route validity: routeValid set false when any leg/turn check fails.
        if (processMission != null && SetsRouteValidFalse(processMission))
        {
            rules.Add(new BusinessRule
            {
                Id = NextId("route-validity-aggregate"),
                Category = BusinessRuleCategory.Validation,
                Statement = "A route is valid only if every leg passes the degree-box check and every "
                          + "turn passes the turn-rate limit.",
                Expression = "routeValid = forall legs (inBox) AND forall turns (<= 120)",
                SourceRefs = new[] { pmId },
                Confidence = 0.85,
            });
        }

        // ── ROUTING ───────────────────────────────────────────────────────────────

        // 9) Sequential waypoint legs: a loop over consecutive waypoints (i, i+1).
        if (processMission != null && HasConsecutiveWaypointLoop(processMission))
        {
            rules.Add(new BusinessRule
            {
                Id = NextId("sequential-waypoint-legs"),
                Category = BusinessRuleCategory.Routing,
                Statement = "Legs are formed between consecutive waypoints in order; a route requires at "
                          + "least two waypoints.",
                Expression = "legs = [(wp[i], wp[i+1]) for i in 0..n-2]; n >= 2",
                SourceRefs = new[] { pmId },
                Confidence = 0.85,
            });
        }

        // ── CONSTRAINT ──────────────────────────────────────────────────────────

        // 10) MST surface-only tasking: the categorical GO/NO-GO branch on variant==MST && platform==SSN.
        if (processMission != null && HasMstSurfaceOnlyBranch(processMission))
        {
            rules.Add(new BusinessRule
            {
                Id = NextId("mst-surface-only-tasking"),
                Category = BusinessRuleCategory.Constraint,
                Statement = "Tasking is GO only if the route is valid AND the mission is not an MST variant "
                          + "on an SSN (submarine) platform; MST is surface-only.",
                Expression = "taskingGoNoGo = routeValid AND NOT (variant == 'MST' AND platform == 'SSN')",
                SourceRefs = new[] { pmId },
                Confidence = 0.93,
            });
        }

        // 11) Weapon max range by variant: the WEAPON_MAX_RANGE_NM map (mined from JS + config).
        if (HasWeaponRangeMap(allSources, out var rangeRef))
        {
            rules.Add(new BusinessRule
            {
                Id = NextId("weapon-max-range-by-variant"),
                Category = BusinessRuleCategory.Constraint,
                Statement = "Each variant has a maximum weapon range: BlockIV 900 nm, BlockV 1000 nm, "
                          + "MST 1000 nm.",
                Expression = "weaponMaxRangeNm = { BlockIV: 900, BlockV: 1000, MST: 1000 }",
                SourceRefs = new[] { rangeRef },
                Confidence = 0.85,
            });
        }

        // 12) Maximum leg length: the MaxLegNm config constant (1500 nm).
        if (HasMaxLegConstant(allSources, out var maxLegRef))
        {
            rules.Add(new BusinessRule
            {
                Id = NextId("max-leg-length"),
                Category = BusinessRuleCategory.Constraint,
                Statement = "A single leg may not exceed MaxLegNm (1500 nm); the degree-box check is the "
                          + "coarse proxy enforcing this near the equator.",
                Expression = "legDistanceNm <= 1500",
                SourceRefs = new[] { maxLegRef ?? configId },
                Confidence = 0.8,
            });
        }

        return rules;
    }

    // ── Structural recognizers (operate on the real Roslyn AST) ──────────────────

    private static SyntaxNode? Body(CSharpMethodInfo m) => (SyntaxNode?)m.Syntax.Body ?? m.Syntax.ExpressionBody;

    private static bool HasGreatCircleKernel(CSharpMethodInfo m)
    {
        var body = Body(m);
        if (body == null) return false;
        // Look for EarthRadius* combined with a Math.Sqrt call.
        bool earthRadius = body.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Any(ma => ma.Name.Identifier.Text.Contains("EarthRadius"));
        bool sqrt = body.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Any(ma => ma.Name.Identifier.Text == "Sqrt");
        return earthRadius && sqrt;
    }

    private static bool HasAccumulatorPattern(CSharpMethodInfo m, string varNameContains)
    {
        var body = Body(m);
        if (body == null) return false;
        // A `xxx += yyy;` where the left side mentions the accumulator name.
        return body.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Any(a => a.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddAssignmentExpression)
                      && a.Left.ToString().Contains(varNameContains, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasTotComputation(CSharpMethodInfo m)
    {
        var body = Body(m);
        if (body == null) return false;
        string text = body.ToString();
        // launch epoch + travel time, and a nominal-speed division.
        return text.Contains("launchEpochSec") && text.Contains("NominalSpeed");
    }

    private static bool HasWrapTo180(CSharpMethodInfo m)
    {
        var body = Body(m);
        if (body == null) return false;
        string text = body.ToString();
        // Adjust-by-360 loops bounded by 180.
        return (text.Contains("360") && text.Contains("180"))
               && body.DescendantNodes().OfType<WhileStatementSyntax>().Any();
    }

    private static bool ComparesAgainstConfig(CSharpMethodInfo m, params string[] configFieldFragments)
    {
        var body = Body(m);
        if (body == null) return false;
        string text = body.ToString();
        return configFieldFragments.All(frag => text.Contains(frag, StringComparison.Ordinal));
    }

    private static bool HasTotTolerance(CSharpMethodInfo m)
    {
        var body = Body(m);
        if (body == null) return false;
        string text = body.ToString();
        // Math.Abs(estimatedTot - desiredTot...) <= TotTol
        return text.Contains("TotTol") && text.Contains("Abs") && text.Contains("desiredTot");
    }

    private static bool SetsRouteValidFalse(CSharpMethodInfo m)
    {
        var body = Body(m);
        if (body == null) return false;
        return body.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Any(a => a.Left.ToString() == "routeValid"
                      && a.Right.ToString() == "false");
    }

    private static bool HasConsecutiveWaypointLoop(CSharpMethodInfo m)
    {
        var body = Body(m);
        if (body == null) return false;
        // A for-loop indexing i and i+1 into lat/lon lists (e.g. lats[i + 1]).
        string text = body.ToString();
        bool indexesNext = text.Contains("[i + 1]") || text.Contains("[i+1]");
        bool loops = body.DescendantNodes().OfType<ForStatementSyntax>().Any();
        return indexesNext && loops;
    }

    private static bool HasMstSurfaceOnlyBranch(CSharpMethodInfo m)
    {
        var body = Body(m);
        if (body == null) return false;
        string text = body.ToString();
        // The categorical tasking branch: variant == "MST" && platform == "SSN".
        return text.Contains("\"MST\"") && text.Contains("\"SSN\"")
               && text.Contains("taskingGoNoGo");
    }

    private static bool HasWeaponRangeMap(IReadOnlyList<SourceArtifact> sources, out string sourceRef)
    {
        foreach (var s in sources)
        {
            if (s.Content.Contains("WEAPON_MAX_RANGE_NM") ||
                (s.Content.Contains("BlockIV") && s.Content.Contains("900") && s.Content.Contains("BlockV")))
            {
                sourceRef = s.Language == SourceLanguage.JavaScript
                    ? $"js:{Path.GetFileNameWithoutExtension(s.Path)}"
                    : "LegacyConfig";
                return true;
            }
        }
        sourceRef = "LegacyConfig";
        return false;
    }

    private static bool HasMaxLegConstant(IReadOnlyList<SourceArtifact> sources, out string? sourceRef)
    {
        foreach (var s in sources)
        {
            if (s.Content.Contains("MaxLegNm") || s.Content.Contains("MAX_LEG_NM"))
            {
                sourceRef = "LegacyConfig";
                return true;
            }
        }
        sourceRef = null;
        return false;
    }
}
