// FORGE EVOLVE for TMPC — Chernoff bound, Equivalence-Composability, and report serialization.

using ForgeEvolve.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace ForgeEvolve.Validation.Tests;

public sealed class BoundsTests
{
    [Fact]
    public void UpperBound_95pct_N2000_IsRuleOfThree_LnTwentyOverN()
    {
        // 95% upper confidence bound for 0 failures in N=2000: ln(1/(1-0.95))/2000 = ln(20)/2000.
        double b = EquivalenceBounds.UpperConfidenceBound(2000, 0.95);
        Assert.Equal(Math.Log(20.0) / 2000.0, b, 1e-9);
        // Sanity: the rule of three, ≈ 3/N ≈ 1.498e-3 (NOT the old mislabeled 5.003e-7).
        Assert.InRange(b, 1.497e-3, 1.499e-3);
    }

    [Fact]
    public void UpperBound_DefaultConfidenceIs95pct()
        => Assert.Equal(EquivalenceBounds.UpperConfidenceBound(2000, 0.95),
                        EquivalenceBounds.UpperConfidenceBound(2000), 1e-15);

    [Fact]
    public void UpperBound_99pct_N2000_IsLnHundredOverN()
        => Assert.Equal(Math.Log(100.0) / 2000.0,
                        EquivalenceBounds.UpperConfidenceBound(2000, 0.99), 1e-9); // ≈ 2.303e-3

    [Fact]
    public void UpperBound_999pct_N2000_IsLnThousandOverN()
    {
        double b = EquivalenceBounds.UpperConfidenceBound(2000, 0.999);
        Assert.Equal(Math.Log(1000.0) / 2000.0, b, 1e-9); // ≈ 3.454e-3
        Assert.InRange(b, 3.45e-3, 3.46e-3);
    }

    [Fact]
    public void UpperBound_HigherConfidence_IsMoreConservative()
        => Assert.True(EquivalenceBounds.UpperConfidenceBound(2000, 0.999)
                       > EquivalenceBounds.UpperConfidenceBound(2000, 0.95));

    [Fact]
    public void UpperBound_ShrinksWithN()
    {
        Assert.True(EquivalenceBounds.UpperConfidenceBound(4000)
                    < EquivalenceBounds.UpperConfidenceBound(2000));
    }

    [Fact]
    public void UpperBound_NoEvidence_IsInfinite()
        => Assert.Equal(double.PositiveInfinity, EquivalenceBounds.UpperConfidenceBound(0));

    [Fact]
    public void Composed_AllUnitInfluenceOne_IsPlainSum()
    {
        double b = EquivalenceBounds.UpperConfidenceBound(2000);
        var units = new[]
        {
            new UnitBound("u1", b),
            new UnitBound("u2", b),
            new UnitBound("u3", b),
        };
        Assert.Equal(3.0 * b, EquivalenceBounds.ComposedSystemBound(units), 15);
    }

    [Fact]
    public void Composed_LipschitzInfluenceScales()
    {
        var units = new[]
        {
            new UnitBound("output-facing", 1e-6, 1.0),
            new UnitBound("attenuated", 1e-6, 0.5),
            new UnitBound("amplifying", 1e-6, 2.0),
        };
        Assert.Equal((1.0 + 0.5 + 2.0) * 1e-6, EquivalenceBounds.ComposedSystemBound(units), 15);
    }

    [Fact]
    public void Composed_NegativeInfluence_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            EquivalenceBounds.ComposedSystemBound(new[] { new UnitBound("bad", 1e-6, -0.1) }));
}

[Collection("corpus")]
public sealed class ReportTests
{
    private readonly CorpusFixture _fx;
    private readonly ITestOutputHelper _out;
    private const string Unit = "MissionRouting.MissionProcessor.ProcessMission";

    public ReportTests(CorpusFixture fx, ITestOutputHelper @out)
    {
        _fx = fx;
        _out = @out;
    }

    [Fact]
    public void Report_SerializesRoundTrips_AndWritesArtifact()
    {
        var validator = new EquivalenceValidator();
        var legacy = new LegacyRunner();
        // Modern == corpus legacyOutput (perfect equivalence) — the report's headline case.
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < _fx.Corpus.Vectors.Count; i++)
            map[_fx.Corpus.Vectors[i].InputJson] = _fx.Corpus.LegacyOutputs[i];
        var modern = new ModernRunner(input => map[input]);

        var report = validator.Verify(Unit, legacy, modern, _fx.Corpus.Vectors, _fx.Tolerance);

        // Attach a worked Equivalence-Composability example (single unit, output-facing L=1).
        var composed = EquivalenceBounds.ComposedSystemBound(
            new[] { new UnitBound(Unit, report.ChernoffDeviationBound, 1.0) });
        report = report with { ComposedSystemBound = composed };

        string json = EquivalenceReportJson.Serialize(report);
        Assert.Contains("\"unitId\"", json);
        Assert.Contains("\"chernoffDeviationBound\"", json);
        Assert.Contains("\"oracles\"", json);

        // Round-trip through System.Text.Json (the contract is designed for defaults).
        var back = System.Text.Json.JsonSerializer.Deserialize<EquivalenceReport>(json,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            });
        Assert.NotNull(back);
        Assert.Equal(report.VectorsTotal, back!.VectorsTotal);
        Assert.Equal(report.Violations, back.Violations);

        // Write the artifact next to the repo's results/ dir (best-effort; under the worktree).
        string? repoRoot = FindRepoRoot();
        string outPath = repoRoot is null
            ? Path.Combine(Path.GetTempPath(), "equivalence-report.json")
            : Path.Combine(repoRoot, "results", "equivalence-report.json");
        string written = EquivalenceReportJson.Write(report, outPath);
        Assert.True(File.Exists(written));

        _out.WriteLine("=== EQUIVALENCE REPORT (modern == corpus legacyOutput) ===");
        _out.WriteLine($"UnitId               : {report.UnitId}");
        _out.WriteLine($"VectorsTotal         : {report.VectorsTotal}");
        _out.WriteLine($"VectorsPassed        : {report.VectorsPassed}");
        _out.WriteLine($"Violations (target 0): {report.Violations}");
        _out.WriteLine($"95% upper confidence bound (rule of three, N={report.VectorsPassed}) = "
            + $"{report.ChernoffDeviationBound:E6}");
        if (report.SecondaryUpperConfidenceBound is double sec)
            _out.WriteLine($"99.9% upper confidence bound = {sec:E6}");
        _out.WriteLine($"ComposedSystemBound (1 unit, L=1) = {report.ComposedSystemBound:E6}");
        _out.WriteLine($"Artifact written     : {written}");

        // Divergence detector headline against the 321-vector ground truth.
        var score = DivergenceDetector.Score(
            _fx.Corpus.Vectors, legacy, _fx.Corpus.ExpectedLegacyDivergent, _fx.Tolerance);
        _out.WriteLine("=== INTENTIONAL-DIVERGENCE DETECTOR vs 321-vector ground truth ===");
        _out.WriteLine($"GroundTruthDivergent : {score.GroundTruthPositives}");
        _out.WriteLine($"TP={score.TruePositives} FP={score.FalsePositives} FN={score.FalseNegatives}");
        _out.WriteLine($"precision={score.Precision:F6}  recall={score.Recall:F6}  F1={score.F1:F6}");
    }

    private static string? FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "ForgeEvolve.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
