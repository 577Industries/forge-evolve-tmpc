// ─────────────────────────────────────────────────────────────────────────────
// Concerns — the candidate microservice boundaries for the surrogate god class.
//
// PART OF: FORGE EVOLVE for TMPC, Migration Planner (Stage 2, workstream WS-C).
//
// HONESTY: these boundaries are HEURISTIC PROPOSALS for the synthetic surrogate, not a validated
// target architecture. A concern is a SEED label attached to god-class members and related modules
// from two signals only: (a) which extracted business rules reference the member, and (b) the
// member/operation name. The spectral (Fiedler) bipartition then partitions the affinity graph; the
// concern labels name the resulting clusters and supply each boundary's candidate API operations.
//
// The four concerns correspond to the four behavioral phases the god method inlines:
//   route validation  -> RouteValidationService
//   time-on-target    -> TotDeconflictionService
//   distribution/SQL  -> MissionDistributionService
//   tasking GO/NO-GO  -> TaskingRulesService
// ─────────────────────────────────────────────────────────────────────────────

namespace ForgeEvolve.Planner;

internal enum Concern
{
    RouteValidation,
    TotDeconfliction,
    MissionDistribution,
    TaskingRules,
    Shared, // serialization/parse glue that does not own a boundary; folded into its caller's unit
}

internal static class Concerns
{
    public static string ServiceName(Concern c) => c switch
    {
        Concern.RouteValidation     => "RouteValidationService",
        Concern.TotDeconfliction    => "TotDeconflictionService",
        Concern.MissionDistribution => "MissionDistributionService",
        Concern.TaskingRules        => "TaskingRulesService",
        _                            => "MissionCoreService",
    };

    public static string UnitId(Concern c) => c switch
    {
        Concern.RouteValidation     => "unit:route-validation",
        Concern.TotDeconfliction    => "unit:tot-deconfliction",
        Concern.MissionDistribution => "unit:mission-distribution",
        Concern.TaskingRules        => "unit:tasking-rules",
        _                            => "unit:mission-core",
    };

    /// <summary>Candidate CLAR-derived API operations exposed at each boundary.</summary>
    public static IReadOnlyList<string> ApiOperations(Concern c) => c switch
    {
        Concern.RouteValidation => new[]
        {
            "ValidateRoute", "CheckLegDegreeBox", "CheckTurnRate", "ComputeInitialBearing", "WrapAntiMeridian",
        },
        Concern.TotDeconfliction => new[]
        {
            "EstimateTimeOnTarget", "ComputeLegDistance", "ComputeTotalDistance", "CheckTotTolerance",
        },
        Concern.MissionDistribution => new[]
        {
            "PublishMission", "UpsertMission", "InsertWaypoints", "BuildMissionResult",
        },
        Concern.TaskingRules => new[]
        {
            "EvaluateTaskingGoNoGo", "CheckMstSurfaceOnly",
        },
        _ => new[] { "ParseMissionRequest" },
    };

    /// <summary>
    /// Concern keywords used to label a module/member by NAME. Rule-attribution is the primary
    /// signal (see MigrationPlanner); this is the fallback / reinforcement signal.
    /// </summary>
    public static Concern? ClassifyByName(string id, string name)
    {
        string s = (id + " " + name);
        bool Has(params string[] ks) => ks.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (Has("publish", "distribut", "missions", "waypoints", "sp_publish", "buildresult", "render", "row"))
            return Concern.MissionDistribution;
        if (Has("tasking", "gonogo", "go_no", "mst"))
            return Concern.TaskingRules;
        if (Has("wrapdlon", "bearing", "turn", "box", "leg", "valid", "route", "milgrid", "grid", "inside"))
            return Concern.RouteValidation;
        if (Has("tot", "time", "distance", "bearing"))
            return Concern.TotDeconfliction;
        return null;
    }

    /// <summary>Map a business-rule id (rule-NN-...) to the concern it belongs to.</summary>
    public static Concern? ClassifyRule(string ruleId, string statement)
    {
        string s = ruleId + " " + statement;
        bool Has(params string[] ks) => ks.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase));

        // Tasking GO/NO-GO is the categorical mission-decision concern (the orchestrator boundary).
        if (Has("tasking", "go-no", "go/no", "surface-only", "mst"))                  return Concern.TaskingRules;
        // Time-on-target deconfliction owns the distance + travel-time + TOT-tolerance math.
        if (Has("time-on-target", "estimated-time", "leap second"))                  return Concern.TotDeconfliction;
        if (Has("great-circle", "total-distance", "leg distance", "tot-tolerance"))  return Concern.TotDeconfliction;
        // Route validation owns the geometric feasibility checks AND the geo helpers (wrap/bearing/turn).
        if (Has("degree-box", "turn-rate", "route-validity", "feasibility", "leg-length",
                "weapon-max-range", "anti-meridian", "wrap", "bearing"))             return Concern.RouteValidation;
        // Distribution owns the publish/output path.
        if (Has("sequential-waypoint", "publish", "distribut"))                      return Concern.MissionDistribution;
        return null;
    }
}
