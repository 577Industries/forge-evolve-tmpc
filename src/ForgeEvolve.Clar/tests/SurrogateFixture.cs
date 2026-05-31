// FORGE EVOLVE for TMPC — test fixture modeling the surrogate's mission processor.
//
// Builds a representative ModuleNode + DiscoveryReport for the synthetic
// MissionProcessor.ProcessMission god method, matching the gold business rules in
// surrogate/gold/business-rules.gold.ttl. No real data — synthetic fixtures only.

using ForgeEvolve.Clar.Model;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Clar.Tests;

internal static class SurrogateFixture
{
    public const string ModuleId = "MissionRouting.MissionProcessor.ProcessMission";

    public static ModuleNode Module() => new()
    {
        Id = ModuleId,
        DisplayName = "MissionProcessor.ProcessMission",
        Kind = ModuleKind.Method,
        Language = SourceLanguage.CSharp,
        SourcePath = "surrogate/tmpc-surrogate-mds/legacy/MissionProcessor.cs",
        StartLine = 78,
        EndLine = 302,
        Complexity = new ComplexityVector
        {
            CyclomaticComplexity = 34,
            LinesOfCode = 225,
            MaxNestingDepth = 4,
            FanIn = 1,
            FanOut = 6,
            CouplingCount = 3,
            TestCoverage = 0.0,
        },
    };

    public static DiscoveryReport Discovery() => new()
    {
        Modules = new[] { Module() },
        Edges = Array.Empty<DependencyEdge>(),
        Sccs = Array.Empty<StronglyConnectedComponent>(),
        BusinessRules = Rules(),
        CryptoFindings = Array.Empty<CryptoFinding>(),
        ParseStatsByLanguage = new Dictionary<string, ParseStats>
        {
            ["CSharp"] = new ParseStats { FilesParsed = 1, FilesTotal = 1 },
        },
    };

    private static IReadOnlyList<BusinessRule> Rules() => new[]
    {
        Rule(ClarConstants.RuleRefs.GreatCircleLegDistance, BusinessRuleCategory.Calculation,
            "Each leg distance is the great-circle distance between consecutive waypoints, using Earth radius 3440.065 nm."),
        Rule(ClarConstants.RuleRefs.MstSurfaceOnlyTasking, BusinessRuleCategory.Constraint,
            "Tasking is GO only if the route is valid AND the mission is not an MST variant on an SSN platform; MST is surface-only."),
        Rule(ClarConstants.RuleRefs.LegDegreeBoxFeasibility, BusinessRuleCategory.Validation,
            "A leg is feasible only if |dLatDeg| <= 22.0 and wrappedAbs(dLonDeg) <= 22.0."),
    };

    private static BusinessRule Rule(string id, BusinessRuleCategory cat, string statement) => new()
    {
        Id = id,
        Category = cat,
        Statement = statement,
        SourceRefs = new[] { ModuleId },
        Confidence = 0.95,
    };
}
