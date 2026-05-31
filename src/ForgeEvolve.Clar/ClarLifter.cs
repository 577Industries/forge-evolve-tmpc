// FORGE EVOLVE for TMPC — CLAR lifter.
//
// Lifts a discovered C# mission-processing module into a four-layer CLAR document. The
// representative nodes model the surrogate's MissionProcessor.ProcessMission god method
// (legacy/MissionProcessor.cs), whose semantics are the single source of truth in
// surrogate/reference/reference.py and whose business rules are hand-labeled in
// surrogate/gold/business-rules.gold.ttl.
//
// LOAD-BEARING FEATURE — precision-constrained data flow:
//   The coordinate (lat/lon), distance (leg/total nm), and time-on-target (TOT) values are
//   C# `double` in the legacy code, FLOAT in the SQL store, and fixed-point Long in the VB6
//   analog. All of these LOSE precision. Each corresponding dataFlow node is emitted with
//   clarType="PrecisionConstrained" and precisionConstrained=true so the target generator
//   MUST emit decimal/checked arithmetic (no float coercion) — this is what repairs the D1
//   (anti-meridian) and D2 (precision-drift) defect classes during modernization.

using ForgeEvolve.Clar.Model;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Clar;

/// <summary>
/// Builds <see cref="ClarDocument"/> instances from discovered modules. The default mapping
/// targets the mission-processing domain modeled by the surrogate; it degrades gracefully
/// for other modules by still populating all four layers with at least skeletal nodes.
/// </summary>
public static class ClarLifter
{
    /// <summary>The abstract CLAR type that forces decimal/checked target arithmetic.</summary>
    public const string PrecisionConstrainedType = "PrecisionConstrained";

    /// <summary>Lift a module (+ discovery context) into an in-memory CLAR document.</summary>
    public static ClarDocument Lift(ModuleNode module, DiscoveryReport context)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(context);

        var doc = new ClarDocument
        {
            Context = JsonLd.ContextNode(),
            ClarVersion = ClarConstants.ClarVersion,
            SourceModuleId = module.Id,
            SourceLanguage = MapLanguage(module.Language),
        };

        AddControlFlow(doc, module);
        AddDataFlow(doc, module);
        AddBusinessLogic(doc, module, context);
        AddInfrastructure(doc, module);

        return doc;
    }

    // ── Control flow ────────────────────────────────────────────────────────────
    // Models the god-method's pipeline: parse -> validate-route loop -> distance loop ->
    // TOT -> tasking branch -> publish. Mirrors the structure of ProcessMission.
    private static void AddControlFlow(ClarDocument doc, ModuleNode module)
    {
        string root = NodeId(module, "cf", "process");
        string parse = NodeId(module, "cf", "parse");
        string routeLoop = NodeId(module, "cf", "route-validation-loop");
        string turnLoop = NodeId(module, "cf", "turn-rate-loop");
        string distLoop = NodeId(module, "cf", "distance-loop");
        string totBranch = NodeId(module, "cf", "tot-feasible-branch");
        string platformSwitch = NodeId(module, "cf", "platform-routing-switch");
        string taskingBranch = NodeId(module, "cf", "tasking-go-no-go-branch");
        string publishBranch = NodeId(module, "cf", "publish-branch");

        doc.ControlFlow.Add(new ControlFlowNode
        {
            Id = root,
            Type = ControlFlowType.PIPELINE,
            Label = "ProcessMission: parse -> validate -> distance -> TOT -> tasking -> publish",
            Children = new() { parse, routeLoop, turnLoop, distLoop, totBranch, taskingBranch, publishBranch },
        });
        doc.ControlFlow.Add(new ControlFlowNode
        {
            Id = parse,
            Type = ControlFlowType.TRY_CATCH,
            Label = "Defensive JSON parse of MissionRequest (swallow-and-message on failure)",
        });
        doc.ControlFlow.Add(new ControlFlowNode
        {
            Id = routeLoop,
            Type = ControlFlowType.FOR_LOOP,
            Label = "Per-leg degree-box feasibility check over consecutive waypoints",
        });
        doc.ControlFlow.Add(new ControlFlowNode
        {
            Id = turnLoop,
            Type = ControlFlowType.FOR_LOOP,
            Label = "Per-turn turn-rate limit check over consecutive bearings",
        });
        doc.ControlFlow.Add(new ControlFlowNode
        {
            Id = distLoop,
            Type = ControlFlowType.FOR_LOOP,
            Label = "Per-leg great-circle distance accumulation (legacy rounds each leg before summing)",
        });
        doc.ControlFlow.Add(new ControlFlowNode
        {
            Id = platformSwitch,
            Type = ControlFlowType.SWITCH,
            Label = "Platform/variant routing-class switch (DDG/CG/SSN x BlockIV/BlockV/MST)",
        });
        doc.ControlFlow.Add(new ControlFlowNode
        {
            Id = totBranch,
            Type = ControlFlowType.BRANCH,
            Label = "TOT feasibility: |estimatedTot - desiredTot| <= TOT_TOL_SEC",
        });
        doc.ControlFlow.Add(new ControlFlowNode
        {
            Id = taskingBranch,
            Type = ControlFlowType.BRANCH,
            Label = "Tasking go/no-go: routeValid AND NOT (MST on SSN)",
            Children = new() { platformSwitch },
        });
        doc.ControlFlow.Add(new ControlFlowNode
        {
            Id = publishBranch,
            Type = ControlFlowType.BRANCH,
            Label = "Guarded inline-SQL publish (PublishEnabled flag; never connects in demo)",
        });
    }

    // ── Data flow ───────────────────────────────────────────────────────────────
    // THE precision-constrained mapping. lat/lon (coordinates), leg/total distance, travel
    // time and TOT are all emitted PrecisionConstrained so the target emits decimal/checked
    // arithmetic. Categorical/integer values are emitted with their plain abstract types.
    private static void AddDataFlow(ClarDocument doc, ModuleNode module)
    {
        // Coordinates — C# double; SQL FLOAT analog; VB6 fixed-point "mil-grid" Long analog.
        doc.DataFlow.Add(Precision(module, "df", "latDeg", DataFlowType.PARAMETER, "double",
            "Waypoint latitude (deg). C# double; SQL FLOAT; VB6 fixed-point mil-grid Long analog."));
        doc.DataFlow.Add(Precision(module, "df", "lonDeg", DataFlowType.PARAMETER, "double",
            "Waypoint longitude (deg). Anti-meridian wrap is precision/correctness critical."));

        // Distance — C# double; SQL FLOAT column; the D2 precision-drift carrier.
        doc.DataFlow.Add(Precision(module, "df", "legDistanceNm", DataFlowType.ARITHMETIC, "double",
            "Per-leg great-circle distance (nm). Legacy rounds each leg before summing (D2)."));
        doc.DataFlow.Add(Precision(module, "df", "totalDistanceNm", DataFlowType.AGGREGATE, "double",
            "Sum of leg distances (nm). SQL stores as FLOAT (precision loss on round-trip)."));
        doc.DataFlow.Add(Precision(module, "df", "earthRadiusNm", DataFlowType.CONSTANT, "double",
            "Earth radius constant 3440.065 nm used in the distance kernel."));

        // Time-on-target — travel seconds and TOT epoch. Continuous-then-integer math whose
        // rounding/truncation is precision-critical (D3 truncates and omits leap seconds).
        doc.DataFlow.Add(Precision(module, "df", "travelSec", DataFlowType.CAST, "double->long",
            "Travel time = totalDistanceNm / 0.15 nm/s; legacy truncates the cast (D3)."));
        doc.DataFlow.Add(Precision(module, "df", "estimatedTotEpochSec", DataFlowType.DATE_TIME, "long",
            "Estimated time-on-target epoch seconds; correct path adds synthetic leap seconds (D3)."));

        // NON precision-constrained data: categorical / integer / text values.
        doc.DataFlow.Add(Plain(module, "df", "routeValid", DataFlowType.RETURN_VALUE, "Boolean", "bool",
            "Aggregate route validity (categorical; preserved exactly vs. reference)."));
        doc.DataFlow.Add(Plain(module, "df", "taskingGoNoGo", DataFlowType.RETURN_VALUE, "Boolean", "bool",
            "Tasking decision (categorical; preserved exactly vs. reference)."));
        doc.DataFlow.Add(Plain(module, "df", "platform", DataFlowType.PARAMETER, "Text", "string",
            "Platform code (DDG/CG/SSN)."));
        doc.DataFlow.Add(Plain(module, "df", "variant", DataFlowType.PARAMETER, "Text", "string",
            "Weapon variant (BlockIV/BlockV/MST)."));
        doc.DataFlow.Add(Plain(module, "df", "messages", DataFlowType.COLLECTION, "TextList", "List<string>",
            "Accumulated diagnostic messages emitted with the result."));
    }

    // ── Business logic ──────────────────────────────────────────────────────────
    // Each node realizes a gold business rule (ruleRef). When the discovery context carries
    // extracted BusinessRules for this module we cite their ids/statements; otherwise we
    // fall back to the canonical gold-rule ids so the layer is always populated.
    private static void AddBusinessLogic(ClarDocument doc, ModuleNode module, DiscoveryReport context)
    {
        Func<string, string?> statementFor = id =>
        {
            foreach (var rule in context.BusinessRules)
                if (string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase))
                    return rule.Statement;
            return null;
        };

        AddRule(doc, module, "bl", "great-circle-leg", BusinessLogicType.CALCULATION,
            ClarConstants.RuleRefs.GreatCircleLegDistance,
            statementFor(ClarConstants.RuleRefs.GreatCircleLegDistance)
                ?? "Each leg distance is the great-circle distance between consecutive waypoints (R = 3440.065 nm).");
        AddRule(doc, module, "bl", "total-distance", BusinessLogicType.CALCULATION,
            ClarConstants.RuleRefs.TotalDistanceSum,
            statementFor(ClarConstants.RuleRefs.TotalDistanceSum)
                ?? "Total distance is the exact sum of all leg distances (no intermediate rounding).");
        AddRule(doc, module, "bl", "anti-meridian-wrap", BusinessLogicType.CALCULATION,
            ClarConstants.RuleRefs.AntiMeridianLongitudeWrap,
            statementFor(ClarConstants.RuleRefs.AntiMeridianLongitudeWrap)
                ?? "Longitude deltas must be wrapped to [-180,180] before use (legacy distance path omits this).");
        AddRule(doc, module, "bl", "estimated-tot", BusinessLogicType.CALCULATION,
            ClarConstants.RuleRefs.EstimatedTimeOnTarget,
            statementFor(ClarConstants.RuleRefs.EstimatedTimeOnTarget)
                ?? "Estimated TOT = launch + round(total/0.15) + leap seconds crossed.");

        AddRule(doc, module, "bl", "degree-box", BusinessLogicType.VALIDATION,
            ClarConstants.RuleRefs.LegDegreeBoxFeasibility,
            statementFor(ClarConstants.RuleRefs.LegDegreeBoxFeasibility)
                ?? "A leg is feasible only if |dLatDeg| <= 22.0 and wrappedAbs(dLonDeg) <= 22.0.");
        AddRule(doc, module, "bl", "turn-rate", BusinessLogicType.VALIDATION,
            ClarConstants.RuleRefs.TurnRateLimit,
            statementFor(ClarConstants.RuleRefs.TurnRateLimit)
                ?? "The turn angle between consecutive legs must not exceed 120 degrees.");
        AddRule(doc, module, "bl", "tot-tolerance", BusinessLogicType.VALIDATION,
            ClarConstants.RuleRefs.TimeOnTargetTolerance,
            statementFor(ClarConstants.RuleRefs.TimeOnTargetTolerance)
                ?? "A route is TOT-feasible only if |estimatedTot - desiredTot| <= 120 s.");
        AddRule(doc, module, "bl", "route-validity", BusinessLogicType.INVARIANT,
            ClarConstants.RuleRefs.RouteValidityAggregate,
            statementFor(ClarConstants.RuleRefs.RouteValidityAggregate)
                ?? "A route is valid only if every leg passes the degree-box check and every turn the turn-rate limit.");

        AddRule(doc, module, "bl", "waypoint-ordering", BusinessLogicType.ROUTING,
            ClarConstants.RuleRefs.WaypointOrderingLegs,
            statementFor(ClarConstants.RuleRefs.WaypointOrderingLegs)
                ?? "Legs are formed between consecutive waypoints in order; a route needs >= 2 waypoints.");

        AddRule(doc, module, "bl", "mst-surface-only", BusinessLogicType.CONSTRAINT,
            ClarConstants.RuleRefs.MstSurfaceOnlyTasking,
            statementFor(ClarConstants.RuleRefs.MstSurfaceOnlyTasking)
                ?? "Tasking is GO only if routeValid AND NOT (variant=MST AND platform=SSN); MST is surface-only.");
        AddRule(doc, module, "bl", "max-leg-length", BusinessLogicType.CONSTRAINT,
            ClarConstants.RuleRefs.MaxLegLength,
            statementFor(ClarConstants.RuleRefs.MaxLegLength)
                ?? "A single leg may not exceed 1500 nm; the degree-box check is the coarse proxy.");
        AddRule(doc, module, "bl", "weapon-range", BusinessLogicType.CONSTRAINT,
            ClarConstants.RuleRefs.WeaponMaxRangeByVariant,
            statementFor(ClarConstants.RuleRefs.WeaponMaxRangeByVariant)
                ?? "Weapon max range by variant: BlockIV 900 nm, BlockV 1000 nm, MST 1000 nm.");

        AddRule(doc, module, "bl", "tasking-classification", BusinessLogicType.CLASSIFICATION,
            ClarConstants.RuleRefs.MstSurfaceOnlyTasking,
            "Mission is classified GO/NO-GO from routeValid and the MST-on-SSN constraint.");
    }

    // ── Infrastructure ──────────────────────────────────────────────────────────
    // The inline-SQL publish (DB_QUERY) and the static mutable config (CONFIGURATION),
    // plus a LOGGING node for the message accumulation.
    private static void AddInfrastructure(ClarDocument doc, ModuleNode module)
    {
        doc.Infrastructure.Add(new InfrastructureNode
        {
            Id = NodeId(module, "infra", "publish-missions"),
            Type = InfrastructureType.DB_QUERY,
            Target = "dbo.Missions (inline ADO.NET INSERT / sp_PublishMission; FLOAT columns)",
        });
        doc.Infrastructure.Add(new InfrastructureNode
        {
            Id = NodeId(module, "infra", "publish-waypoints"),
            Type = InfrastructureType.DB_QUERY,
            Target = "dbo.Waypoints (N+1 per-leg INSERT; FLOAT lat/lon/leg columns)",
        });
        doc.Infrastructure.Add(new InfrastructureNode
        {
            Id = NodeId(module, "infra", "legacy-config"),
            Type = InfrastructureType.CONFIGURATION,
            Target = "LegacyConfig (static mutable: EarthRadiusNm, MaxTurnDeg, NominalSpeed, TotTolSec, PublishEnabled)",
        });
        doc.Infrastructure.Add(new InfrastructureNode
        {
            Id = NodeId(module, "infra", "result-messages"),
            Type = InfrastructureType.LOGGING,
            Target = "MissionResult.messages diagnostic trail (LEG_OUT_OF_BOX / TURN_EXCEEDED / ...)",
        });
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    // The trailing 'note' carries the human rationale for the mapping. The dataFlowNode
    // schema has no free-text description slot, so the note is documentary only (it keeps
    // the call sites self-explanatory); the abstract type lives in clarType/sourceType.
    private static DataFlowNode Precision(ModuleNode module, string prefix, string name,
        DataFlowType type, string sourceType, string note)
    {
        _ = note;
        return new DataFlowNode
        {
            Id = NodeId(module, prefix, name),
            Type = type,
            Name = name,
            ClarType = PrecisionConstrainedType,
            PrecisionConstrained = true,
            SourceType = sourceType,
        };
    }

    private static DataFlowNode Plain(ModuleNode module, string prefix, string name,
        DataFlowType type, string clarType, string sourceType, string note)
    {
        _ = note;
        return new DataFlowNode
        {
            Id = NodeId(module, prefix, name),
            Type = type,
            Name = name,
            ClarType = clarType,
            PrecisionConstrained = false,
            SourceType = sourceType,
        };
    }

    private static void AddRule(ClarDocument doc, ModuleNode module, string prefix, string slug,
        BusinessLogicType type, string ruleRef, string statement)
        => doc.BusinessLogic.Add(new BusinessLogicNode
        {
            Id = NodeId(module, prefix, slug),
            Type = type,
            RuleRef = ruleRef,
            Statement = statement,
        });

    private static string NodeId(ModuleNode module, string layer, string slug)
        => $"clar:{module.Id}#{layer}/{slug}";

    private static string MapLanguage(SourceLanguage lang) => lang switch
    {
        SourceLanguage.CSharp => "CSharp",
        SourceLanguage.JavaScript => "JavaScript",
        SourceLanguage.Sql => "Sql",
        SourceLanguage.Vb6 => "Vb6",
        SourceLanguage.Cobol => "Cobol",
        SourceLanguage.Fortran => "Fortran",
        SourceLanguage.Ada => "Ada",
        SourceLanguage.Java => "Java",
        // The schema's sourceLanguage enum has no "Unknown"; default to CSharp (the TMPC
        // focus language) so emitted documents always validate.
        _ => "CSharp",
    };
}
