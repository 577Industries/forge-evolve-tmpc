// FORGE EVOLVE for TMPC — corpus-driven differential equivalence tests (acceptance a–e).
//
// These prove the ENGINE is correct against the frozen golden corpus + the surrogate legacy
// runner. The headline equivalence number for the REAL modern component is produced later at
// integration (P3); here we exercise the engine with corpus-derived modern stand-ins.

using ForgeEvolve.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace ForgeEvolve.Validation.Tests;

[Collection("corpus")]
public sealed class EquivalenceValidatorTests
{
    private readonly CorpusFixture _fx;
    private readonly ITestOutputHelper _out;
    private const string Unit = "MissionRouting.MissionProcessor.ProcessMission";

    public EquivalenceValidatorTests(CorpusFixture fx, ITestOutputHelper @out)
    {
        _fx = fx;
        _out = @out;
    }

    /// <summary>A modern runner that echoes the corpus legacyOutput keyed by input — i.e. a
    /// modern that behaves bit-identically to legacy. Used for the perfect-equivalence cases.</summary>
    private IModernRunner LegacyEchoModern()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < _fx.Corpus.Vectors.Count; i++)
            map[_fx.Corpus.Vectors[i].InputJson] = _fx.Corpus.LegacyOutputs[i];
        return new ModernRunner(input => map[input]);
    }

    // ── (a) legacy-vs-legacy => 0 violations, VectorsPassed == N ─────────────────────
    [Fact]
    public void A_LegacyVsLegacy_ZeroViolations_AllPass()
    {
        var validator = new EquivalenceValidator();
        var legacy = new LegacyRunner();
        // Modern == the SAME legacy implementation.
        var modern = new ModernRunner(input => MissionProcessorEcho.Legacy(input));

        var report = validator.Verify(Unit, legacy, modern, _fx.Corpus.Vectors, _fx.Tolerance);

        Assert.Equal(0, report.Violations);
        Assert.Equal(_fx.Corpus.Vectors.Count, report.VectorsPassed);
        Assert.Equal(_fx.Corpus.Vectors.Count, report.VectorsTotal);
        // No oracle should report any violation when both sides are the legacy implementation.
        Assert.All(report.Oracles, o => Assert.Equal(0, o.Violations));
    }

    // ── (b) modern == corpus legacyOutput => 0 violations, Chernoff = ln(1/0.999)/N ──
    [Fact]
    public void B_ModernEqualsCorpusLegacyOutput_ZeroViolations_ChernoffExact()
    {
        var validator = new EquivalenceValidator();
        var legacy = new LegacyRunner();
        var modern = LegacyEchoModern();

        var report = validator.Verify(Unit, legacy, modern, _fx.Corpus.Vectors, _fx.Tolerance);

        int n = _fx.Corpus.Vectors.Count;
        Assert.Equal(0, report.Violations);
        Assert.Equal(n, report.VectorsPassed);

        double expected = Math.Log(1.0 / 0.999) / n;
        Assert.Equal(expected, report.ChernoffDeviationBound, 15);
        Assert.Equal(0.999, report.ConfidenceLevel, 12);
        _out.WriteLine($"Chernoff bound (N={n}, delta=0.999) = {report.ChernoffDeviationBound:E6}");
    }

    // ── (c) modern perturbed beyond tolerance on a continuous field => > 0 violations ─
    [Fact]
    public void C_ModernPerturbedContinuous_ProducesViolations()
    {
        var validator = new EquivalenceValidator();
        var legacy = new LegacyRunner();

        // Modern = legacy output with totalDistanceNm (and leg 0) scaled by 1% — wildly past 1e-9.
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < _fx.Corpus.Vectors.Count; i++)
            map[_fx.Corpus.Vectors[i].InputJson] =
                MissionGenerators.PerturbContinuous(_fx.Corpus.LegacyOutputs[i], 1.01);
        var modern = new ModernRunner(input => map[input]);

        var report = validator.Verify(Unit, legacy, modern, _fx.Corpus.Vectors, _fx.Tolerance);

        Assert.True(report.Violations > 0,
            "Perturbing a continuous output 1% beyond the 1e-9 tolerance must be caught.");
        // The totalDistanceNm oracle should account for violations on every multi-... vector
        // (every vector has a nonzero total distance, so a 1% scale always violates).
        var total = report.Oracles.Single(o => o.OracleName == Oracles.Names.TotalDistanceNm);
        Assert.True(total.Violations > 0);
        Assert.True(total.MaxObservedRelativeError > _fx.Tolerance.ContinuousRelativeError);
        _out.WriteLine($"Perturbed-modern violations: {report.Violations} "
            + $"(totalDistanceNm oracle: {total.Violations})");
    }

    // ── (d) intentional-divergence detector flags EXACTLY the corpus divergent set ────
    [Fact]
    public void D_DivergenceDetector_MatchesGroundTruth_321()
    {
        var legacy = new LegacyRunner();
        var score = DivergenceDetector.Score(
            _fx.Corpus.Vectors, legacy, _fx.Corpus.ExpectedLegacyDivergent, _fx.Tolerance);

        _out.WriteLine($"Divergence detector vs ground truth ({score.GroundTruthPositives} divergent):");
        _out.WriteLine($"  TP={score.TruePositives} FP={score.FalsePositives} FN={score.FalseNegatives}");
        _out.WriteLine($"  precision={score.Precision:F6} recall={score.Recall:F6} F1={score.F1:F6}");

        Assert.Equal(321, score.GroundTruthPositives);
        Assert.Equal(321, score.TruePositives);
        Assert.Equal(0, score.FalsePositives);
        Assert.Equal(0, score.FalseNegatives);
        Assert.Equal(1.0, score.Precision, 12);
        Assert.Equal(1.0, score.Recall, 12);
    }

    /// <summary>Same detection, but expressed through the validator's oracle pipeline: with the
    /// CORRECT reference answer key AS modern, NO vector becomes an unexpected violation — every
    /// place the reference modern differs from legacy is a place legacy was wrong, i.e. an
    /// INTENTIONAL DIVERGENCE (a finding). This is the central honesty guarantee.</summary>
    [Fact]
    public void D2_ReferenceAsModern_DivergencesAreFindingsNotViolations()
    {
        var validator = new EquivalenceValidator();
        var legacy = new LegacyRunner();
        // Modern = the CORRECT reference output (carried in ExpectedOutputJson).
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in _fx.Corpus.Vectors) map[v.InputJson] = v.ExpectedOutputJson;
        var modern = new ModernRunner(input => map[input]);

        var report = validator.Verify(Unit, legacy, modern, _fx.Corpus.Vectors, _fx.Tolerance);

        // THE key claim: a correct modern that fixes the legacy bugs produces ZERO unexpected
        // violations — every legacy/modern disagreement is classified as an intentional finding.
        Assert.Equal(0, report.Violations);

        // VectorsPassed = vectors where legacy already matched the reference on EVERY oracle
        // field (including the raw estimatedTotEpochSec, which the engine compares exactly even
        // though the corpus's operational-divergence LABEL counts only the totFeasible flip).
        // So passed + intentional-divergent == N, and passed is strictly below the count of
        // operationally-non-divergent vectors (the engine is MORE precise than the label).
        Assert.True(report.VectorsPassed > 0);
        Assert.True(report.VectorsPassed < _fx.Corpus.Vectors.Count - _fx.Corpus.DivergentCount,
            "Exact estimatedTotEpochSec comparison makes the engine flag more findings than the "
            + "corpus operational label; passes must be below N - operationalDivergent.");

        // Every divergent vector (per the corpus label) is reproduced as a finding, never a
        // violation: the operational divergences are a SUBSET of the engine's intentional set.
        var totFeasible = report.Oracles.Single(o => o.OracleName == Oracles.Names.TotFeasible);
        Assert.True(totFeasible.IsIntentionalDivergence);
        Assert.Equal(0, totFeasible.Violations);
        _out.WriteLine($"reference-as-modern: VectorsPassed={report.VectorsPassed}, "
            + $"Violations={report.Violations} (corpus operational-divergent={_fx.Corpus.DivergentCount})");
    }

    // ── (e) discrete oracle tolerance 0: a totFeasible flip is caught ─────────────────
    [Fact]
    public void E_DiscreteOracle_TotFeasibleFlip_IsCaught()
    {
        var validator = new EquivalenceValidator();
        var legacy = new LegacyRunner();

        // Modern = legacy output but with totFeasible flipped on every vector.
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < _fx.Corpus.Vectors.Count; i++)
            map[_fx.Corpus.Vectors[i].InputJson] =
                MissionGenerators.FlipTotFeasible(_fx.Corpus.LegacyOutputs[i]);
        var modern = new ModernRunner(input => map[input]);

        var report = validator.Verify(Unit, legacy, modern, _fx.Corpus.Vectors, _fx.Tolerance);

        var totFeasible = report.Oracles.Single(o => o.OracleName == Oracles.Names.TotFeasible);
        Assert.Equal(OracleKind.Discrete, totFeasible.Kind);
        // Every vector's totFeasible was flipped; with tolerance 0 every one is a violation,
        // UNLESS the flip happens to coincide with the reference (intentional). Since legacy ==
        // reference on totFeasible for non-divergent vectors, flipping creates a violation there.
        int nonDivergent = _fx.Corpus.Vectors.Count - _fx.Corpus.DivergentCount;
        Assert.True(totFeasible.Violations >= nonDivergent,
            $"Expected >= {nonDivergent} totFeasible violations, got {totFeasible.Violations}.");
        Assert.True(report.Violations > 0);
        _out.WriteLine($"totFeasible-flip violations: {totFeasible.Violations} of {report.VectorsTotal}");
    }
}

/// <summary>Tiny direct bridge to the surrogate legacy processor for the legacy-vs-legacy test
/// (kept here so the test file does not need a <c>using Tmpc.Surrogate.Legacy</c> at the top).</summary>
internal static class MissionProcessorEcho
{
    public static string Legacy(string inputJson)
        => Tmpc.Surrogate.Legacy.MissionProcessor.ProcessMission(inputJson);
}
