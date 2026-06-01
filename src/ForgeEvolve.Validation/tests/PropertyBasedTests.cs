// FORGE EVOLVE for TMPC — property-based equivalence tests (CsCheck).
//
// Beyond the frozen corpus, these generate thousands of RANDOM well-formed MissionRequests and
// assert two engine invariants:
//
//   * when modern == legacy (byte-identical output), the DISCRETE oracles NEVER report a
//     violation and the vector always PASSES;
//   * perturbing a CONTINUOUS output beyond tolerance is always CAUGHT (a violation appears).
//
// Inputs are synthetic (random, fixed-distribution) — no real data.

using ForgeEvolve.Contracts;
using CsCheck;
using Xunit;

namespace ForgeEvolve.Validation.Tests;

public sealed class PropertyBasedTests
{
    private static readonly ToleranceConfig Tol = new();
    private const string Unit = "MissionRouting.MissionProcessor.ProcessMission";

    /// <summary>Build a single-vector test set from a generated input, with the legacy output
    /// as the reference answer key (so a legacy-equals-modern run has no divergence to find).</summary>
    private static (IReadOnlyList<EquivalenceTestVector> vectors, string legacyOut) OneVector(string inputJson)
    {
        string legacyOut = Tmpc.Surrogate.Legacy.MissionProcessor.ProcessMission(inputJson);
        var v = new[]
        {
            new EquivalenceTestVector
            {
                Id = "GEN",
                InputJson = inputJson,
                ExpectedOutputJson = legacyOut, // reference == legacy => no divergence expected
                Tags = new[] { "nominal" },
            },
        };
        return (v, legacyOut);
    }

    // ── PROPERTY 1: modern == legacy => discrete oracles never violate; vector passes ─
    [Fact]
    public void Property_ModernEqualsLegacy_NeverViolates()
    {
        MissionGenerators.NominalMissionJson.Sample(inputJson =>
        {
            var validator = new EquivalenceValidator();
            var legacy = new LegacyRunner();
            var modern = new ModernRunner(
                input => Tmpc.Surrogate.Legacy.MissionProcessor.ProcessMission(input));

            var (vectors, _) = OneVector(inputJson);
            var report = validator.Verify(Unit, legacy, modern, vectors, Tol);

            // Identical implementations => zero violations, the single vector passes, and every
            // discrete oracle in particular is clean.
            bool discreteClean = report.Oracles
                .Where(o => o.Kind == OracleKind.Discrete)
                .All(o => o.Violations == 0);

            return report.Violations == 0
                   && report.VectorsPassed == 1
                   && discreteClean;
        }, iter: 2000);
    }

    // ── PROPERTY 2: perturbing a continuous output beyond tolerance is caught ─────────
    [Fact]
    public void Property_PerturbedContinuous_AlwaysCaught()
    {
        // Pair each generated input with a perturbation factor at least ~1e-6 away from 1.0,
        // which is >1000x the 1e-9 relative tolerance.
        var gen =
            from inputJson in MissionGenerators.NominalMissionJson
            from sign in Gen.Bool
            from mag in Gen.Double[1e-6, 0.5]
            select (inputJson, factor: 1.0 + (sign ? mag : -mag));

        gen.Sample(pair =>
        {
            var (inputJson, factor) = pair;
            var validator = new EquivalenceValidator();
            var legacy = new LegacyRunner();

            var (vectors, legacyOut) = OneVector(inputJson);

            // Skip degenerate routes whose total distance is ~0 (nothing to perturb meaningfully).
            var parsed = MissionResult.Parse(legacyOut);
            if (Math.Abs(parsed.TotalDistanceNm) < 1e-6) return true;

            string perturbed = MissionGenerators.PerturbContinuous(legacyOut, factor);
            var modern = new ModernRunner(_ => perturbed);

            var report = validator.Verify(Unit, legacy, modern, vectors, Tol);

            // A continuous output pushed well past 1e-9 MUST surface as a violation.
            return report.Violations > 0;
        }, iter: 2000);
    }
}
