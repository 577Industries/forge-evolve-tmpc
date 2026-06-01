// ─────────────────────────────────────────────────────────────────────────────
// CliSupport — small, deterministic glue for the integration driver.
//
// Nothing here changes any module's public API: these are CLI-local helpers for argument
// parsing, repo-root location, a deterministic Turtle writer for the extracted business rules,
// a TransformResult serializer, and the canonical-JSON / SHA helpers used for provenance payloads.
// All output is stable (ordinal sorting, invariant culture, no timestamps) so the demo is
// byte-reproducible.
// ─────────────────────────────────────────────────────────────────────────────

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Cli;

/// <summary>Thrown for bad/missing CLI arguments (mapped to a usage message + exit 2).</summary>
internal sealed class CliUsageException : Exception
{
    public CliUsageException(string message) : base(message) { }
}

/// <summary>Parsed CLI options: --surrogate &lt;dir&gt; --out &lt;dir&gt; --mode &lt;offline|local|cloud&gt;.</summary>
internal sealed record CliOptions
{
    public required string Surrogate { get; init; }
    public required string Out { get; init; }
    public required OrchestratorMode Mode { get; init; }

    public static CliOptions Parse(string[] args)
    {
        string? surrogate = null, outDir = null, mode = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--surrogate": surrogate = Next(args, ref i, "--surrogate"); break;
                case "--out": outDir = Next(args, ref i, "--out"); break;
                case "--mode": mode = Next(args, ref i, "--mode"); break;
                default:
                    throw new CliUsageException($"Unknown argument '{args[i]}'.");
            }
        }
        if (string.IsNullOrWhiteSpace(surrogate)) throw new CliUsageException("--surrogate <dir> is required.");
        if (string.IsNullOrWhiteSpace(outDir)) throw new CliUsageException("--out <dir> is required.");
        mode ??= "offline";
        if (!Enum.TryParse<OrchestratorMode>(mode, ignoreCase: true, out var parsedMode))
            throw new CliUsageException($"--mode must be one of offline|local|cloud (got '{mode}').");

        return new CliOptions { Surrogate = surrogate!, Out = outDir!, Mode = parsedMode };
    }

    private static string Next(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length) throw new CliUsageException($"{flag} requires a value.");
        return args[++i];
    }
}

/// <summary>Locates the worktree root (directory containing ForgeEvolve.sln).</summary>
internal static class RepoRoot
{
    public static string Locate(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory());
        for (int i = 0; i < 16 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ForgeEvolve.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        // Fall back to the assembly location's ancestry (covers `dotnet run` from odd cwds).
        dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 16 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ForgeEvolve.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate ForgeEvolve.sln above cwd or the CLI assembly.");
    }
}

/// <summary>
/// Deterministic Turtle (TTL) renderer for the extracted business rules. Mirrors the gold-file
/// shape (rule:&lt;id&gt; a fe:BusinessRule ; fe:category ... ; rdfs:label ... ; fe:statement ... ;
/// [fe:expression ...] ; fe:sourceRef ... .) so the artifact is reviewer-diffable against the gold.
/// No timestamps; rules are emitted in the order Discovery produced them.
/// </summary>
internal static class BusinessRulesTtl
{
    public static string Render(IReadOnlyList<BusinessRule> rules)
    {
        var sb = new StringBuilder();
        sb.Append("# business-rules.ttl — rules extracted by the FORGE EVOLVE Discovery engine\n");
        sb.Append("# from the synthetic, unclassified surrogate. Scored (F1) against\n");
        sb.Append("# surrogate/gold/business-rules.gold.ttl. Deterministic; no timestamps.\n\n");
        sb.Append("@prefix fe:   <https://577industries.com/forge-evolve/ns#> .\n");
        sb.Append("@prefix rule: <https://577industries.com/forge-evolve/surrogate/rule#> .\n");
        sb.Append("@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\n\n");

        foreach (BusinessRule r in rules)
        {
            sb.Append("rule:").Append(SafeLocalName(r.Id)).Append(" a fe:BusinessRule ;\n");
            sb.Append("    fe:category ").Append(Lit(r.Category.ToString())).Append(" ;\n");
            sb.Append("    rdfs:label ").Append(Lit(r.Id)).Append(" ;\n");
            sb.Append("    fe:statement ").Append(Lit(r.Statement)).Append(" ;\n");
            sb.Append("    fe:confidence ").Append(Lit(r.Confidence.ToString("F3", CultureInfo.InvariantCulture))).Append(" ;\n");
            if (!string.IsNullOrEmpty(r.Expression))
                sb.Append("    fe:expression ").Append(Lit(r.Expression!)).Append(" ;\n");
            foreach (string srcRef in r.SourceRefs)
                sb.Append("    fe:sourceRef ").Append(Lit(srcRef)).Append(" ;\n");
            // Replace the trailing " ;\n" with " .\n\n".
            sb.Length -= 2;
            sb.Append(".\n\n");
        }
        return sb.ToString();
    }

    private static string SafeLocalName(string id)
    {
        var chars = id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        string s = new string(chars).Trim('_');
        return s.Length == 0 ? "rule" : s;
    }

    private static string Lit(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}

/// <summary>Serializes a TransformResult to deterministic indented JSON (camelCase).</summary>
internal static class TransformResultJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(TransformResult result) => JsonSerializer.Serialize(result, Options);
}

/// <summary>Builds canonical, deterministic JSON for governance provenance payloads.</summary>
internal static class Canonical
{
    /// <summary>
    /// Build a compact, key-ordered JSON object from (key,value) pairs. Values may be string,
    /// bool, int, long, or double. Ordinal key ordering + invariant formatting keep the payload
    /// hash reproducible across runs.
    /// </summary>
    public static string Json(params (string Key, object Value)[] fields)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        bool first = true;
        foreach (var (key, value) in fields.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(Str(key)).Append(':').Append(Format(value));
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string Format(object v) => v switch
    {
        bool b => b ? "true" : "false",
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        string s => Str(s),
        _ => Str(v.ToString() ?? ""),
    };

    private static string Str(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}

/// <summary>SHA-256 hex helper (lowercase), for provenance payload digests.</summary>
internal static class Sha
{
    public static string Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>One latent-defect class with its detected divergent-vector count.</summary>
internal sealed record LatentDefectByClass(string Tag, string Description, int DetectedCount);

/// <summary>
/// The latent-defect detection result over the full corpus (Fix B): the per-class divergent-vector
/// counts the validation oracle surfaces (legacy-vs-reference) plus the detector's precision/recall
/// against the corpus ground truth. Serialized deterministically to results/run/latent-defects.json
/// (no timestamps / RNG) so the demo is byte-reproducible.
/// </summary>
internal sealed record LatentDefectReport
{
    public required int CorpusVectors { get; init; }
    public required int TotalDetected { get; init; }
    public required int GroundTruthDivergent { get; init; }
    public required double Precision { get; init; }
    public required double Recall { get; init; }
    public required IReadOnlyList<LatentDefectByClass> ByClass { get; init; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(LatentDefectReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        // Project to an anonymous, ordered shape so the artifact is stable and self-describing.
        var doc = new
        {
            description =
                "Latent legacy defects surfaced by the mission-data-aware equivalence oracle "
                + "(legacy output vs the independent reference answer key) over the full corpus. "
                + "Each is an ECP-recommended finding for human adjudication — never auto-fixed.",
            detector = "ForgeEvolve.Validation.DivergenceDetector",
            report.CorpusVectors,
            report.TotalDetected,
            report.GroundTruthDivergent,
            report.Precision,
            report.Recall,
            byClass = report.ByClass.Select(c => new
            {
                c.Tag,
                c.Description,
                c.DetectedCount,
            }),
            disclaimer =
                "measured on the synthetic, unclassified surrogate; preliminary; not government-validated.",
        };
        return JsonSerializer.Serialize(doc, Options);
    }
}
