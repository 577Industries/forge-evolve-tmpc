// ─────────────────────────────────────────────────────────────────────────────
// RiskModel — composite modernization-risk score in [0,1] per module.
//
// PART OF: FORGE EVOLVE for TMPC, Migration Planner (Stage 2, workstream WS-C).
//
// The composite score is a WEIGHTED BLEND of normalized debt/criticality factors. The weights are
// the FORGE EVOLVE spec factor weights (they sum to 1.0); each factor is squashed into [0,1] before
// blending so the composite is always in [0,1]:
//
//   factor                weight   source signal (from the frozen ComplexityVector + heuristics)
//   ───────────────────── ──────   ──────────────────────────────────────────────────────────────
//   cyclomatic complexity  0.20    CyclomaticComplexity, saturating at CcSaturation (≈30, the
//                                  pre-registered god-method threshold) -> 1.0
//   dependency depth       0.18    FanOut (call/coupling out-degree) saturating at DepthSaturation
//   business criticality   0.17    domain criticality of the module (mission-decision code scores
//                                  higher than pure formatting/helpers) — see BusinessCriticality
//   test-coverage deficit  0.15    1 - TestCoverage  (no tests => full deficit)
//   data coupling          0.12    CouplingCount (direct DB calls / global-config reads) saturating
//   language obscurity     0.08    legacy-language penalty (VB6 > SQL > JS > C#)
//   age proxy              0.05    code-age proxy from size/nesting (older, larger, deeper => riskier)
//   documentation gap      0.05    inverse of an XML/comment-density proxy (here a fixed legacy gap)
//
// HONESTY: the surrogate ships no test coverage, no git history, and no doc-comment corpus, so the
// age and doc-gap factors are deterministic PROXIES (documented below), not measured values. They are
// low-weight by design. Everything is computed from the frozen DiscoveryReport — no external data.
// ─────────────────────────────────────────────────────────────────────────────

using ForgeEvolve.Contracts;

namespace ForgeEvolve.Planner;

/// <summary>The eight FORGE EVOLVE risk-factor weights (sum = 1.0) and their saturation constants.</summary>
internal static class RiskWeights
{
    public const double Cyclomatic        = 0.20;
    public const double DependencyDepth    = 0.18;
    public const double BusinessCriticality = 0.17;
    public const double TestCoverageDeficit = 0.15;
    public const double DataCoupling       = 0.12;
    public const double LanguageObscurity  = 0.08;
    public const double Age                = 0.05;
    public const double DocGap             = 0.05;

    public static double Sum =>
        Cyclomatic + DependencyDepth + BusinessCriticality + TestCoverageDeficit +
        DataCoupling + LanguageObscurity + Age + DocGap;

    // Saturation points: the factor value at which the normalized signal reaches 1.0.
    public const double CcSaturation       = 30.0;  // pre-registered god-method CC threshold
    public const double DepthSaturation    = 30.0;  // fan-out at which dependency depth is maximal
    public const double CouplingSaturation = 8.0;   // distinct data/global couplings -> maximal
    public const double LocSaturation      = 200.0; // LOC contributing to the age proxy
    public const double NestSaturation     = 6.0;   // nesting depth contributing to the age proxy
}

internal static class RiskModel
{
    /// <summary>Linear saturation: value/cap clamped to [0,1].</summary>
    internal static double Saturate(double value, double cap) =>
        cap <= 0 ? 0.0 : Math.Clamp(value / cap, 0.0, 1.0);

    /// <summary>
    /// Domain business-criticality of a module in [0,1]. Mission-DECISION code (route validation,
    /// tasking GO/NO-GO, time-on-target, the publish/distribution path) is more operationally
    /// critical than pure formatting/serialization helpers. Derived from the module id/name and any
    /// business rules attributed to it — NOT hand-tuned per surrogate beyond these documented classes.
    /// </summary>
    internal static double BusinessCriticality(ModuleNode m, int attributedRuleCount)
    {
        string id = m.Id;
        string name = m.DisplayName;

        // Base criticality by what the module *is*.
        double baseScore =
            ContainsAny(id, name, "ProcessMission", "Mission", "Tasking", "Publish", "sp_Publish") ? 0.95 :
            ContainsAny(id, name, "Validate", "Route", "Box", "Turn", "Bearing", "Leg")            ? 0.80 :
            ContainsAny(id, name, "Tot", "Time", "Distance", "GreatCircle", "Wrap", "Milgrid", "Grid") ? 0.70 :
            ContainsAny(id, name, "BuildResult", "Render", "Row", "Json", "Config")                ? 0.35 :
            0.50;

        // Modules that more business rules were extracted from are more business-critical.
        double ruleBoost = RiskModel.Saturate(attributedRuleCount, 4.0) * 0.20;
        return Math.Clamp(baseScore + ruleBoost, 0.0, 1.0);
    }

    /// <summary>Legacy-language obscurity penalty in [0,1] (harder-to-modernize stacks score higher).</summary>
    internal static double LanguageObscurity(SourceLanguage lang) => lang switch
    {
        SourceLanguage.Vb6        => 1.00, // dead language, no modern runtime
        SourceLanguage.Cobol      => 1.00,
        SourceLanguage.Fortran    => 0.90,
        SourceLanguage.Ada        => 0.80,
        SourceLanguage.Sql        => 0.60, // T-SQL stored-proc logic to extract
        SourceLanguage.JavaScript => 0.40, // untyped, but live
        SourceLanguage.Java       => 0.30,
        SourceLanguage.CSharp     => 0.20, // already on/near the target stack
        _                          => 0.50,
    };

    /// <summary>
    /// Age proxy in [0,1]. The surrogate carries no VCS history, so we proxy "age/entrenchment"
    /// from structural heaviness: large, deeply-nested modules are the long-lived, accreted code
    /// that is riskiest to move. Blend of normalized LOC and nesting depth.
    /// </summary>
    internal static double AgeProxy(ComplexityVector c) =>
        0.6 * Saturate(c.LinesOfCode, RiskWeights.LocSaturation) +
        0.4 * Saturate(c.MaxNestingDepth, RiskWeights.NestSaturation);

    /// <summary>
    /// Documentation-gap proxy in [0,1]. No doc-comment corpus is available for the surrogate, so we
    /// model the known legacy documentation gap as a deterministic constant per language family
    /// (legacy languages are the least documented). Low weight (0.05) keeps this honest.
    /// </summary>
    internal static double DocGap(SourceLanguage lang) => lang switch
    {
        SourceLanguage.Vb6 or SourceLanguage.Cobol or SourceLanguage.Fortran => 0.90,
        SourceLanguage.Sql                                                    => 0.70,
        SourceLanguage.JavaScript                                             => 0.60,
        _                                                                      => 0.50,
    };

    /// <summary>
    /// Composite risk in [0,1] for one module. <paramref name="attributedRuleCount"/> is the number
    /// of extracted business rules that reference this module (more rules => more business-critical).
    /// </summary>
    public static double Score(ModuleNode m, int attributedRuleCount)
    {
        var c = m.Complexity;

        double fCyclomatic = Saturate(c.CyclomaticComplexity, RiskWeights.CcSaturation);
        double fDepth      = Saturate(c.FanOut, RiskWeights.DepthSaturation);
        double fCritical   = BusinessCriticality(m, attributedRuleCount);
        double fCovDeficit = Math.Clamp(1.0 - c.TestCoverage, 0.0, 1.0);
        double fCoupling   = Saturate(c.CouplingCount, RiskWeights.CouplingSaturation);
        double fLang       = LanguageObscurity(m.Language);
        double fAge        = AgeProxy(c);
        double fDoc        = DocGap(m.Language);

        double score =
            RiskWeights.Cyclomatic         * fCyclomatic +
            RiskWeights.DependencyDepth     * fDepth +
            RiskWeights.BusinessCriticality * fCritical +
            RiskWeights.TestCoverageDeficit * fCovDeficit +
            RiskWeights.DataCoupling        * fCoupling +
            RiskWeights.LanguageObscurity   * fLang +
            RiskWeights.Age                 * fAge +
            RiskWeights.DocGap              * fDoc;

        // Weights sum to 1.0 and every factor is in [0,1], so the blend is already in [0,1];
        // clamp defensively against floating-point drift.
        return Math.Clamp(score, 0.0, 1.0);
    }

    private static bool ContainsAny(string id, string name, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (id.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
