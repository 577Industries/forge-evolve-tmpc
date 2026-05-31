// ─────────────────────────────────────────────────────────────────────────────
// SurrogateFixture — loads the ACTUAL surrogate files and runs Discovery once per test class.
//
// PART OF: FORGE EVOLVE for TMPC, Discovery Engine tests (workstream WS-A).
//
// Walks up from the test assembly location to the worktree root (the directory containing
// ForgeEvolve.sln), then reads the real surrogate C#/JS/SQL/VB6 and the gold TTL. No fixtures are
// fabricated — the tests analyze the same files shipped in surrogate/.
// ─────────────────────────────────────────────────────────────────────────────

using ForgeEvolve.Contracts;
using ForgeEvolve.Discovery;

namespace ForgeEvolve.Discovery.Tests;

public sealed class SurrogateFixture
{
    public IReadOnlyList<SourceArtifact> Sources { get; }
    public string GoldTtl { get; }
    public DiscoveryReport Report { get; }
    public RuleF1Report F1 { get; }
    public string RepoRoot { get; }

    public SurrogateFixture()
    {
        RepoRoot = FindRepoRoot();
        string surrogate = Path.Combine(RepoRoot, "surrogate", "tmpc-surrogate-mds");

        var files = new[]
        {
            Path.Combine(surrogate, "legacy", "MissionProcessor.cs"),
            Path.Combine(surrogate, "legacy", "wwwroot", "mission-review.js"),
            Path.Combine(surrogate, "legacy", "sql", "sp_PublishMission.sql"),
            Path.Combine(surrogate, "legacy", "sql", "schema.sql"),
            Path.Combine(surrogate, "legacy", "GeoFixedPoint.bas"),
        };
        foreach (var f in files)
            if (!File.Exists(f)) throw new FileNotFoundException($"Surrogate file missing: {f}");

        Sources = files.Select(SourceLoader.FromFile).ToList();
        GoldTtl = File.ReadAllText(Path.Combine(RepoRoot, "surrogate", "gold", "business-rules.gold.ttl"));

        var engine = new DiscoveryEngine();
        (Report, F1) = engine.AnalyzeWithF1(Sources, GoldTtl);
    }

    private static string FindRepoRoot()
    {
        // Start at the test binary directory and walk up to the dir holding ForgeEvolve.sln.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ForgeEvolve.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate the worktree root (no ForgeEvolve.sln found above the test assembly).");
    }
}
