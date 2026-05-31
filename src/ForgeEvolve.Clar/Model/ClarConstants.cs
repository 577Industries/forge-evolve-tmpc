// FORGE EVOLVE for TMPC — CLAR constants.
//
// Central, single-source spellings for the frozen vocabulary so the lifter and tests
// reference the same literals. The schema pins clarVersion to "0.1.0" (a const), so we
// pin it here too; the JSON-LD context IRI matches the published CLAR vocabulary base.

namespace ForgeEvolve.Clar.Model;

/// <summary>Frozen constants for CLAR document emission.</summary>
public static class ClarConstants
{
    /// <summary>The only clarVersion the schema accepts ("const": "0.1.0").</summary>
    public const string ClarVersion = "0.1.0";

    /// <summary>
    /// JSON-LD context IRI for the published CLAR vocabulary. Emitted as the document's
    /// "@context". The schema accepts either a string IRI (this) or an inline object.
    /// </summary>
    public const string ContextIri = "https://577industries.com/forge-evolve/clar/v0.1.0/context.jsonld";

    /// <summary>
    /// ruleRef identifiers for the gold business rules in
    /// surrogate/gold/business-rules.gold.ttl. The lifter cites these so a downstream
    /// transformer can join CLAR business-logic nodes back to the extracted RDF rules.
    /// </summary>
    public static class RuleRefs
    {
        public const string GreatCircleLegDistance = "rule:GreatCircleLegDistance";
        public const string TotalDistanceSum = "rule:TotalDistanceSum";
        public const string EstimatedTimeOnTarget = "rule:EstimatedTimeOnTarget";
        public const string AntiMeridianLongitudeWrap = "rule:AntiMeridianLongitudeWrap";
        public const string LegDegreeBoxFeasibility = "rule:LegDegreeBoxFeasibility";
        public const string TurnRateLimit = "rule:TurnRateLimit";
        public const string TimeOnTargetTolerance = "rule:TimeOnTargetTolerance";
        public const string RouteValidityAggregate = "rule:RouteValidityAggregate";
        public const string WaypointOrderingLegs = "rule:WaypointOrderingLegs";
        public const string MstSurfaceOnlyTasking = "rule:MstSurfaceOnlyTasking";
        public const string WeaponMaxRangeByVariant = "rule:WeaponMaxRangeByVariant";
        public const string MaxLegLength = "rule:MaxLegLength";
    }
}
