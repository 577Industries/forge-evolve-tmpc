// Shared fixture: loads the REAL synthetic surrogate files from the repo so every STIG
// finding under test is genuinely present in the analyzed code (no fabricated inputs).

using System.Security.Cryptography;
using System.Text;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Cato.Tests;

public static class SurrogateFixture
{
    /// <summary>Walk up from the test assembly to the repo root (the dir containing surrogate/).</summary>
    public static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "surrogate")) &&
                File.Exists(Path.Combine(dir, "ForgeEvolve.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName!;
        }
        throw new DirectoryNotFoundException("Could not locate repo root (surrogate/ + ForgeEvolve.sln).");
    }

    public static string Sha256Hex(string s)
    {
        byte[] h = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(h.Length * 2);
        foreach (byte b in h) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static SourceArtifact Load(string relPath, SourceLanguage lang)
    {
        string full = Path.Combine(RepoRoot(), relPath);
        string content = File.ReadAllText(full);
        return new SourceArtifact
        {
            Path = relPath.Replace('\\', '/'),
            Language = lang,
            Content = content,
            ContentSha256 = Sha256Hex(content),
        };
    }

    /// <summary>The three real legacy surrogate files the overlay scans.</summary>
    public static IReadOnlyList<SourceArtifact> Legacy() => new[]
    {
        Load("surrogate/tmpc-surrogate-mds/legacy/MissionProcessor.cs", SourceLanguage.CSharp),
        Load("surrogate/tmpc-surrogate-mds/legacy/sql/sp_PublishMission.sql", SourceLanguage.Sql),
        Load("surrogate/tmpc-surrogate-mds/legacy/wwwroot/mission-review.js", SourceLanguage.JavaScript),
    };

    /// <summary>
    /// A clean "modern" emitted set: parameterized repository, config-injected connection,
    /// validated input, set-based SQL, and escaped output. None of the legacy finding classes
    /// are present, so all should reconcile to RemediatedByTransform=true.
    /// </summary>
    public static IReadOnlyList<EmittedFile> CleanModern() => new[]
    {
        new EmittedFile
        {
            Path = "modern/MissionRepository.cs",
            Language = SourceLanguage.CSharp,
            Content = """
                using Microsoft.Data.SqlClient;
                namespace Tmpc.Modern;
                public sealed class MissionRepository
                {
                    private readonly string _connectionString; // injected, not embedded
                    public MissionRepository(IOptions<DbOptions> o) => _connectionString = o.Value.ConnectionString;
                    public async Task PublishAsync(Mission m, CancellationToken ct)
                    {
                        await using var conn = new SqlConnection(_connectionString);
                        // Parameterized, single set-based call via a table-valued parameter.
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = "dbo.sp_PublishMission_V2";
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@Mission", SqlDbType.Structured) { Value = m.ToTable() });
                        await conn.OpenAsync(ct);
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
                """,
        },
        new EmittedFile
        {
            Path = "modern/sp_PublishMission_V2.sql",
            Language = SourceLanguage.Sql,
            Content = """
                CREATE PROCEDURE dbo.sp_PublishMission_V2
                    @Mission dbo.MissionTvp READONLY
                AS
                BEGIN
                    SET NOCOUNT ON;
                    BEGIN TRANSACTION;
                    INSERT INTO dbo.Waypoints (MissionId, Seq, LatDeg, LonDeg, LegDistanceNm)
                    SELECT MissionId, Seq, LatDeg, LonDeg, LegDistanceNm FROM @Mission;
                    COMMIT TRANSACTION;
                END
                """,
        },
        new EmittedFile
        {
            Path = "modern/mission-review.tsx",
            Language = SourceLanguage.JavaScript,
            Content = """
                // React renders via the virtual DOM; values are escaped by default.
                export function MissionReview({ request, result }) {
                    const go = serverTasking(result); // single source of truth from server contract
                    return createElement("table", null,
                        createElement("tbody", null,
                            row("Mission", request.missionId),
                            row("Tasking", go ? "GO" : "NO-GO")));
                }
                """,
        },
    };

    /// <summary>
    /// A demo-faithful "modern" emitted set: the SQL-injection class (string-concatenated
    /// SqlCommand) is genuinely fixed (parameterized), so APSC-DV-002500 is REMEDIATED — but the
    /// modern C# still carries the TLS (TrustServerCertificate=true) and hardcoded connection-string
    /// patterns, so APSC-DV-001620 and APSC-DV-002400 remain RESIDUAL. There are no modern .js/.sql
    /// emitted files, so the legacy JS-XSS (002490) and SQL-DDL (002560) findings are OUT-OF-SCOPE
    /// (the modern C# component does not cover those file types — absence ≠ fix). This reproduces
    /// the real demo disposition: 1 remediated / 2 out-of-scope / 2 residual.
    /// </summary>
    public static IReadOnlyList<EmittedFile> ModernDemoLike() => new[]
    {
        new EmittedFile
        {
            Path = "tmpc-modern-mds/src/Distribution/MissionPublisher.cs",
            Language = SourceLanguage.CSharp,
            Content = """
                using Microsoft.Data.SqlClient;
                namespace Tmpc.Modern.Distribution;
                public sealed class MissionPublisher
                {
                    // RESIDUAL hardening items still present in the modern C#:
                    //   * hardcoded connection string (APSC-DV-002400)
                    //   * TrustServerCertificate=true (APSC-DV-001620)
                    private const string ConnectionString =
                        "Server=(localdb)\\TmpcModern;Initial Catalog=Tmpc;Integrated Security=true;TrustServerCertificate=true";
                    public async Task PublishAsync(Mission m, CancellationToken ct)
                    {
                        await using var conn = new SqlConnection(ConnectionString);
                        // SQL-injection class is REMEDIATED: parameterized, no string-concatenated SqlCommand text.
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = "dbo.sp_PublishMission_V2";
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@MissionId", SqlDbType.Int) { Value = m.Id });
                        await conn.OpenAsync(ct);
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
                """,
        },
    };

    /// <summary>
    /// A representative discovery report carrying the recovered business rules for the three
    /// latent computational defects (D1/D2/D3), so the POA&amp;M lists only defects present.
    /// </summary>
    public static DiscoveryReport Discovery() => new()
    {
        Modules = Array.Empty<ModuleNode>(),
        Edges = Array.Empty<DependencyEdge>(),
        Sccs = Array.Empty<StronglyConnectedComponent>(),
        BusinessRules = new[]
        {
            new BusinessRule
            {
                Id = "BR-D1-antimeridian",
                Category = BusinessRuleCategory.Calculation,
                Statement = "Leg distance must wrap longitude delta (anti-meridian) across +/-180 degrees.",
                Expression = "dLon = wrap(lon2 - lon1)",
                SourceRefs = new[] { "MissionProcessor.ProcessMission" },
                Confidence = 0.92,
            },
            new BusinessRule
            {
                Id = "BR-D2-precision",
                Category = BusinessRuleCategory.Calculation,
                Statement = "Distance accumulation must avoid intermediate rounding (precision drift); FLOAT persistence loses precision.",
                Expression = "total = sum(legs) without per-leg round",
                SourceRefs = new[] { "MissionProcessor.ProcessMission", "schema.sql" },
                Confidence = 0.88,
            },
            new BusinessRule
            {
                Id = "BR-D3-tot",
                Category = BusinessRuleCategory.Calculation,
                Statement = "Time-on-target must round travel time and apply leap-second epoch adjustment.",
                Expression = "estimatedTot = launch + round(travel) + leapSeconds",
                SourceRefs = new[] { "MissionProcessor.ProcessMission" },
                Confidence = 0.90,
            },
        },
        CryptoFindings = Array.Empty<CryptoFinding>(),
        ParseStatsByLanguage = new Dictionary<string, ParseStats>(),
    };
}
