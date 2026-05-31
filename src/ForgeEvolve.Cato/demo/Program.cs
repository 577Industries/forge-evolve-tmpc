// CatoDemo — runs the Cyber/cATO overlay over the REAL synthetic surrogate and prints the
// acceptance metrics (STIG before/after counts, Merkle root, mapped control IDs).
//
// Usage: dotnet run --project src/ForgeEvolve.Cato/demo  [outputDir]
// Default outputDir: <repo>/results/run/cato  (gitignored; reproducible)

using System.Security.Cryptography;
using System.Text;
using ForgeEvolve.Cato;
using ForgeEvolve.Contracts;

static string RepoRoot()
{
    string dir = AppContext.BaseDirectory;
    for (int i = 0; i < 12; i++)
    {
        if (Directory.Exists(Path.Combine(dir, "surrogate")) &&
            File.Exists(Path.Combine(dir, "ForgeEvolve.sln")))
            return dir;
        dir = Directory.GetParent(dir)?.FullName
              ?? throw new DirectoryNotFoundException("repo root not found");
    }
    throw new DirectoryNotFoundException("repo root not found");
}

static string Sha256Hex(string s)
{
    byte[] h = SHA256.HashData(Encoding.UTF8.GetBytes(s));
    var sb = new StringBuilder(h.Length * 2);
    foreach (byte b in h) sb.Append(b.ToString("x2"));
    return sb.ToString();
}

string root = RepoRoot();
SourceArtifact Load(string rel, SourceLanguage lang)
{
    string content = File.ReadAllText(Path.Combine(root, rel));
    return new SourceArtifact
    {
        Path = rel.Replace('\\', '/'),
        Language = lang,
        Content = content,
        ContentSha256 = Sha256Hex(content),
    };
}

var legacy = new[]
{
    Load("surrogate/tmpc-surrogate-mds/legacy/MissionProcessor.cs", SourceLanguage.CSharp),
    Load("surrogate/tmpc-surrogate-mds/legacy/sql/sp_PublishMission.sql", SourceLanguage.Sql),
    Load("surrogate/tmpc-surrogate-mds/legacy/wwwroot/mission-review.js", SourceLanguage.JavaScript),
};

// Clean modern sample (parameterized, validated, escaped) so the overlay can show remediation.
var modern = new[]
{
    new EmittedFile
    {
        Path = "modern/MissionRepository.cs",
        Language = SourceLanguage.CSharp,
        Content =
            "public sealed class MissionRepository {\n" +
            "  private readonly string _cs; // injected, not embedded\n" +
            "  public MissionRepository(IOptions<DbOptions> o){ _cs = o.Value.ConnectionString; }\n" +
            "  public Task PublishAsync(Mission m) => _repo.ExecuteAsync(\"dbo.sp_PublishMission_V2\", m.ToTvp());\n" +
            "}\n",
    },
    new EmittedFile
    {
        Path = "modern/sp_PublishMission_V2.sql",
        Language = SourceLanguage.Sql,
        Content =
            "CREATE PROCEDURE dbo.sp_PublishMission_V2 @Mission dbo.MissionTvp READONLY AS\n" +
            "BEGIN\n  BEGIN TRANSACTION;\n" +
            "  INSERT INTO dbo.Waypoints (MissionId, Seq, LatDeg, LonDeg, LegDistanceNm)\n" +
            "  SELECT MissionId, Seq, LatDeg, LonDeg, LegDistanceNm FROM @Mission;\n" +
            "  COMMIT TRANSACTION;\nEND\n",
    },
    new EmittedFile
    {
        Path = "modern/mission-review.tsx",
        Language = SourceLanguage.JavaScript,
        Content =
            "export function MissionReview({request, result}) {\n" +
            "  const go = serverTasking(result); // single source of truth\n" +
            "  return createElement('table', null, row('Tasking', go ? 'GO' : 'NO-GO'));\n" +
            "}\n",
    },
};

var discovery = new DiscoveryReport
{
    Modules = Array.Empty<ModuleNode>(),
    Edges = Array.Empty<DependencyEdge>(),
    Sccs = Array.Empty<StronglyConnectedComponent>(),
    BusinessRules = new[]
    {
        new BusinessRule { Id = "BR-D1", Category = BusinessRuleCategory.Calculation,
            Statement = "Leg distance must wrap longitude delta (anti-meridian).",
            Expression = "dLon = wrap(lon2-lon1)", SourceRefs = new[] { "MissionProcessor" }, Confidence = 0.9 },
        new BusinessRule { Id = "BR-D2", Category = BusinessRuleCategory.Calculation,
            Statement = "Avoid intermediate rounding (precision drift); FLOAT persistence loses precision.",
            Expression = "total=sum(legs)", SourceRefs = new[] { "MissionProcessor", "schema.sql" }, Confidence = 0.9 },
        new BusinessRule { Id = "BR-D3", Category = BusinessRuleCategory.Calculation,
            Statement = "Time-on-target must round travel time and apply leap-second epoch adjustment.",
            Expression = "tot=launch+round(travel)+leap", SourceRefs = new[] { "MissionProcessor" }, Confidence = 0.9 },
    },
    CryptoFindings = Array.Empty<CryptoFinding>(),
    ParseStatsByLanguage = new Dictionary<string, ParseStats>(),
};

// Default under results/run/ (which is gitignored — generated artifacts are reproducible).
string outDir = args.Length > 0 ? args[0] : Path.Combine(root, "results", "run", "cato");

var overlay = new CyberOverlay();
CatoArtifacts art = overlay.Generate(legacy, modern, discovery, outDir);

Console.WriteLine("=== FORGE EVOLVE for TMPC — Cyber/cATO overlay (WS-G) acceptance ===");
Console.WriteLine($"Output directory          : {Path.GetFullPath(outDir)}");
Console.WriteLine($"STIG findings (before)    : {art.StigBefore.Count}");
Console.WriteLine($"STIG findings (after)     : {art.StigAfter.Count} " +
                  $"(remediated: {art.StigAfter.Count(f => f.RemediatedByTransform)}, " +
                  $"open: {art.StigAfter.Count(f => !f.RemediatedByTransform)})");
Console.WriteLine($"Provenance Merkle root    : {art.ProvenanceMerkleRoot}");
Console.WriteLine($"Control IDs mapped        : {string.Join(", ", art.ControlMap.Select(c => c.ControlId))}");
Console.WriteLine($"POA&M items               : {art.Poam.Count} " +
                  $"(open: {art.Poam.Count(p => p.Status == "Open")}, " +
                  $"remediated: {art.Poam.Count(p => p.Status == "Remediated")})");
Console.WriteLine();
Console.WriteLine("STIG findings (before) detail:");
foreach (StigFinding f in art.StigBefore)
    Console.WriteLine($"  [{f.Severity,-6}] {f.RuleId,-15} {f.Location}");
Console.WriteLine();
Console.WriteLine("POA&M (computational defects, ECP-recommended):");
foreach (PoamItem p in art.Poam.Where(p => p.Id.StartsWith("POAM-C", StringComparison.Ordinal)))
    Console.WriteLine($"  {p.Id} [{p.Status}] {p.Weakness.Split('.')[0]}.");

return 0;
