// Injected, immutable options — the modern replacement for the legacy static mutable
// `LegacyConfig` global-state smell (DEBT.md item 2). Tunables and the (output-neutral) publish
// configuration are passed in via constructor injection instead of being read from global statics
// deep inside processing code.
//
// SECURITY (output-neutral publish path only): the hardcoded connection string with
// TrustServerCertificate=true is GONE. The connection string is injected (default null), and the
// publish path stays disabled by default (PublishEnabled=false) so the demo never connects.

namespace ForgeEvolve.ModernMds.Models;

/// <summary>
/// Physical/business tunables for the mission processor. Values match the legacy
/// <c>LegacyConfig</c> exactly so behavior is preserved; they are now injected and immutable.
/// </summary>
public sealed record MissionOptions
{
    public double EarthRadiusNm { get; init; } = 3440.065;
    public double MaxLegNm { get; init; } = 1500.0;
    public double MaxTurnDeg { get; init; } = 120.0;
    public double NominalSpeedNmPerSec { get; init; } = 0.15;
    public int TotTolSec { get; init; } = 120;
    public double MaxLegDLatDeg { get; init; } = 22.0;
    public double MaxLegDLonDeg { get; init; } = 22.0;

    /// <summary>D2 rounding precision (decimal places). Preserved from the legacy quirk.</summary>
    public int LegacyLegDecimals { get; init; } = 8;

    /// <summary>
    /// Synthetic leap-second boundaries (fictional placeholders). The legacy estimator OMITS this
    /// adjustment (D3); the table is retained so the ECP-recommended fix has the data available.
    /// </summary>
    public IReadOnlyList<long> SyntheticLeapBoundaries { get; init; } = new long[]
    {
        1_000_000_000L, 1_100_000_000L, 1_200_000_000L, 2_000_000_000L, 4_000_000_000L,
    };

    /// <summary>The injected publish configuration (output-neutral; disabled by default).</summary>
    public PublishOptions Publish { get; init; } = new();
}

/// <summary>
/// Output-neutral publish configuration. Hardened vs. the legacy: NO hardcoded connection string
/// and NO TrustServerCertificate=true. The connection string is injected (null by default) and the
/// path is disabled by default, so the demo never opens a connection.
/// </summary>
public sealed record PublishOptions
{
    /// <summary>When false (the demo default) the publish path never connects.</summary>
    public bool PublishEnabled { get; init; } = false;

    /// <summary>
    /// Injected connection string (no embedded credentials/secrets here, and no
    /// TrustServerCertificate=true). Null by default — required only when PublishEnabled=true.
    /// </summary>
    public string? ConnectionString { get; init; } = null;
}
