// FORGE EVOLVE for TMPC — Governance workstream (WS-H) tests.
//
// Gate-logic tests pinned to the pre-registered thresholds (pre-registration.md):
//   * KG1 passes with F1 >= 0.85 + oracle harness; fails when F1 < 0.85.
//   * KG2 passes with 0 discrete violations + cATO bundle; fails otherwise.
//   * Each evaluation records a provenance entry (provenance of the decision).

using ForgeEvolve.Contracts;
using ForgeEvolve.Governance;
using Xunit;

namespace ForgeEvolve.Governance.Tests;

public sealed class ReviewGateTests
{
    private static IReadOnlyDictionary<string, string> Ev(params (string K, string V)[] pairs)
        => pairs.ToDictionary(p => p.K, p => p.V, StringComparer.Ordinal);

    [Fact]
    public void KG1_Passes_WhenF1AtLeast085_AndOracleHarnessRuns()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("KG1", Ev(("ruleF1", "0.90"), ("oracleHarnessRuns", "true")));

        Assert.Equal("KG1", gate.GateId);
        Assert.True(gate.Passed);
        Assert.Equal("0.90", gate.Evidence["ruleF1"]);
    }

    [Fact]
    public void KG1_PassesAtExactThreshold_085()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("KG1", Ev(("ruleF1", "0.85"), ("oracleHarnessRuns", "true")));
        Assert.True(gate.Passed);
    }

    [Fact]
    public void KG1_Fails_WhenF1BelowThreshold()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("KG1", Ev(("ruleF1", "0.50"), ("oracleHarnessRuns", "true")));
        Assert.False(gate.Passed);
    }

    [Fact]
    public void KG1_Fails_WhenOracleHarnessDidNotRun()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("KG1", Ev(("ruleF1", "0.95"), ("oracleHarnessRuns", "false")));
        Assert.False(gate.Passed);
    }

    [Fact]
    public void KG1_Fails_WhenEvidenceMissing()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("KG1", Ev(("ruleF1", "0.95"))); // no oracleHarnessRuns key
        Assert.False(gate.Passed);
    }

    [Fact]
    public void KG2_Passes_WhenZeroDiscreteViolations_AndCatoBundle()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("KG2", Ev(("discreteViolations", "0"), ("catoBundle", "true")));
        Assert.True(gate.Passed);
    }

    [Fact]
    public void KG2_Fails_WhenDiscreteViolationsNonZero()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("KG2", Ev(("discreteViolations", "3"), ("catoBundle", "true")));
        Assert.False(gate.Passed);
    }

    [Fact]
    public void KG2_Fails_WhenCatoBundleMissing()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("KG2", Ev(("discreteViolations", "0"), ("catoBundle", "false")));
        Assert.False(gate.Passed);
    }

    [Fact]
    public void DesignGate_Passes_WithApprovalBoundaryAndScope()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("Design", Ev(
            ("humanApproved", "true"),
            ("boundaryApproved", "true"),
            ("unitScope", "MissionRouting")));
        Assert.True(gate.Passed);
    }

    [Fact]
    public void DesignGate_Fails_WithoutHumanApproval()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("Design", Ev(
            ("humanApproved", "false"),
            ("boundaryApproved", "true"),
            ("unitScope", "MissionRouting")));
        Assert.False(gate.Passed);
    }

    [Fact]
    public void TranslationGate_Passes_WhenCodeReviewedRulesHonoredAndCompiledClean()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("Translation", Ev(
            ("humanApproved", "true"),
            ("diffReviewed", "true"),
            ("rulesHonored", "true"),
            ("compiledClean", "true")));
        Assert.True(gate.Passed);
    }

    [Fact]
    public void AcceptanceGate_Passes_WithZeroDiscreteContinuousOkAndCatoReviewed()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("Acceptance", Ev(
            ("humanApproved", "true"),
            ("discreteViolations", "0"),
            ("continuousWithinTolerance", "true"),
            ("catoDeltaReviewed", "true")));
        Assert.True(gate.Passed);
    }

    [Fact]
    public void AcceptanceGate_Fails_WhenDiscreteViolationsPresent()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("Acceptance", Ev(
            ("humanApproved", "true"),
            ("discreteViolations", "1"),
            ("continuousWithinTolerance", "true"),
            ("catoDeltaReviewed", "true")));
        Assert.False(gate.Passed);
    }

    [Fact]
    public void UnknownGate_FailsClosed()
    {
        var gov = new GovernanceService();
        var gate = gov.Evaluate("NOT_A_GATE", Ev(("anything", "true")));
        Assert.False(gate.Passed);
        Assert.Contains("Unknown gate", gate.Description);
    }

    [Fact]
    public void EvaluatingAGate_RecordsAProvenanceEntry()
    {
        var gov = new GovernanceService();
        Assert.Empty(gov.Ledger.Records);

        gov.Evaluate("KG1", Ev(("ruleF1", "0.90"), ("oracleHarnessRuns", "true")));

        Assert.Single(gov.Ledger.Records);
        var entry = gov.Ledger.Records[0];
        Assert.Equal(ReviewGateEvaluator.GateDecisionAction, entry.Action);
        Assert.Equal("governance:gate:KG1", entry.Actor);
        Assert.True(gov.Verify().Valid);
    }

    [Fact]
    public void GateDecisionPayload_IsDeterministic_RegardlessOfEvidenceInsertionOrder()
    {
        // Two evidence dictionaries with the same pairs in different insertion order must produce
        // the same recorded entry hash (canonical, ordinally-sorted payload).
        var govA = new GovernanceService();
        govA.Evaluate("KG2", Ev(("discreteViolations", "0"), ("catoBundle", "true")));

        var govB = new GovernanceService();
        govB.Evaluate("KG2", Ev(("catoBundle", "true"), ("discreteViolations", "0")));

        Assert.Equal(
            govA.Ledger.Records[0].EntryHash,
            govB.Ledger.Records[0].EntryHash);
    }
}
