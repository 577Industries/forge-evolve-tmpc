namespace ForgeEvolve.Orchestrator;

/// <summary>
/// Draws samples from a Beta(α, β) distribution using only <see cref="Random"/> — no external
/// numerics dependency, so the Offline path stays pure C# with no network and no npm reliance.
/// </summary>
/// <remarks>
/// Beta(α, β) is sampled as X / (X + Y) where X ~ Gamma(α, 1) and Y ~ Gamma(β, 1). Gamma draws
/// use the Marsaglia–Tsang method, which is fast and accurate for shape ≥ 1; for shape &lt; 1 we
/// boost the shape and apply the standard u^(1/α) correction. Because the supplied
/// <see cref="Random"/> is seedable, every sample sequence is reproducible — load-bearing for the
/// deterministic reviewer demo and the unit tests.
/// </remarks>
public static class BetaSampler
{
    /// <summary>Sample one value in (0,1) from Beta(<paramref name="alpha"/>, <paramref name="beta"/>).</summary>
    public static double Sample(double alpha, double beta, Random rng)
    {
        if (alpha <= 0) throw new ArgumentOutOfRangeException(nameof(alpha), "alpha must be > 0.");
        if (beta <= 0) throw new ArgumentOutOfRangeException(nameof(beta), "beta must be > 0.");
        ArgumentNullException.ThrowIfNull(rng);

        var x = SampleGamma(alpha, rng);
        var y = SampleGamma(beta, rng);
        var denom = x + y;
        // Guard the (astronomically unlikely) double-underflow case.
        return denom <= 0 ? 0.5 : x / denom;
    }

    /// <summary>Gamma(shape, scale=1) via Marsaglia–Tsang, with the shape&lt;1 boosting trick.</summary>
    private static double SampleGamma(double shape, Random rng)
    {
        if (shape < 1.0)
        {
            // Boost: if G ~ Gamma(shape+1) and U ~ Uniform(0,1), then G * U^(1/shape) ~ Gamma(shape).
            var g = SampleGamma(shape + 1.0, rng);
            var u = rng.NextDouble();
            // Avoid pow(0, ...) producing 0 collapse; NextDouble() is in [0,1).
            return g * Math.Pow(u <= 0 ? double.Epsilon : u, 1.0 / shape);
        }

        var d = shape - 1.0 / 3.0;
        var c = 1.0 / Math.Sqrt(9.0 * d);

        while (true)
        {
            double x, v;
            do
            {
                x = SampleStandardNormal(rng);
                v = 1.0 + c * x;
            }
            while (v <= 0);

            v = v * v * v;
            var u = rng.NextDouble();
            var x2 = x * x;

            // Squeeze test then full acceptance test.
            if (u < 1.0 - 0.0331 * x2 * x2)
                return d * v;
            if (Math.Log(u) < 0.5 * x2 + d * (1.0 - v + Math.Log(v)))
                return d * v;
        }
    }

    /// <summary>Standard normal sample via the Box–Muller transform.</summary>
    private static double SampleStandardNormal(Random rng)
    {
        // u1 in (0,1] to keep Log well-defined.
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
