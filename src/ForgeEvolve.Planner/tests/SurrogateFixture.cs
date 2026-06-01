// ─────────────────────────────────────────────────────────────────────────────
// SurrogateFixture — runs the REAL Discovery engine on the ACTUAL surrogate, then the REAL Planner.
//
// PART OF: FORGE EVOLVE for TMPC, Migration Planner tests (workstream WS-C).
//
// No fixtures are fabricated: the tests analyze the same surrogate files shipped under surrogate/ via
// the real ForgeEvolve.Discovery engine, then plan the resulting DiscoveryReport with the real
// ForgeEvolve.Planner. This is the end-to-end Discovery -> Planner contract path.
// ─────────────────────────────────────────────────────────────────────────────

using ForgeEvolve.Contracts;
using ForgeEvolve.Discovery;
using ForgeEvolve.Planner;

namespace ForgeEvolve.Planner.Tests;

public sealed class SurrogateFixture
{
    public IReadOnlyList<SourceArtifact> Sources { get; }
    public DiscoveryReport Report { get; }
    public MigrationPlan Plan { get; }
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
        Report = new DiscoveryEngine().Analyze(Sources);
        Plan = new MigrationPlanner().Plan(Report);
    }

    /// <summary>The god method discovered in the surrogate (CC well above 30).</summary>
    public ModuleNode GodMethod => Report.Modules.Single(m =>
        m.Kind == ModuleKind.Method && m.DisplayName == "ProcessMission");

    private static string FindRepoRoot()
    {
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
