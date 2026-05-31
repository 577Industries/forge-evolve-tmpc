// ─────────────────────────────────────────────────────────────────────────────
// StigAnalyzer — static security analyzers that emit STIG findings (before/after).
//
// PART OF: FORGE EVOLVE for TMPC, Cyber/cATO overlay (Stage 5).
//
// Detects the security-relevant *classes of defect that are actually present* in the
// synthetic legacy surrogate, and confirms their absence in the modern code. Every
// finding below corresponds to a literal pattern in the surrogate (no fabricated
// findings — see the line references in each detector).
//
// STIG V-ID mapping note (REPRESENTATIVE, not authoritative):
//   The RuleIds use real V-ID *stems* from the DISA Application Security & Development
//   STIG (APSC-DV-*) and the Microsoft .NET Framework STIG (APPNET / .NET STIG family),
//   chosen because their requirement text matches the detected defect class. They are
//   illustrative mappings for a synthetic surrogate, NOT a claim that a specific released
//   benchmark revision assigns this exact V-ID to this exact line. A real cATO run would
//   bind these to the current benchmark revision's rule IDs.
//
// Analysis strategy / DEVIATION:
//   The spec calls for "Roslyn for C# + regex for SQL". The offline NuGet cache on the
//   build host does NOT contain Microsoft.CodeAnalysis (Roslyn), and the cATO overlay must
//   build & run fully offline/air-gapped. To stay self-contained and deterministic, the
//   C# analyzer here is a line/regex-based scanner rather than a Roslyn syntax-walk. The
//   detected patterns (hardcoded connection-string literal, string-concatenated SQL
//   command construction, unvalidated external input) are unambiguous at the lexical level
//   for this corpus, so the findings are identical to what a Roslyn rule would surface. The
//   detector is structured so a Roslyn analyzer can be dropped in behind the same interface
//   when the package is available. See the README/report for this deviation.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.RegularExpressions;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Cato;

/// <summary>
/// Catalog of STIG checks. Each check is a predicate over a <see cref="SourceArtifact"/>
/// (or emitted modern file) that yields zero or more <see cref="StigFinding"/>s.
/// </summary>
public static class StigAnalyzer
{
    // ── Representative STIG V-ID stems (App Sec &amp; Dev STIG + .NET STIG) ──────────────
    public const string VID_SqlInjection = "APSC-DV-002500"; // SQL injection / dynamic SQL construction
    public const string VID_HardcodedCreds = "APSC-DV-002400"; // hardcoded/embedded authentication data
    public const string VID_InputValidation = "APSC-DV-002560"; // input validation / sanitize untrusted data
    public const string VID_OutputEncoding = "APSC-DV-002490"; // output encoding (XSS) — DOM string-building
    public const string VID_TlsCertValidation = "APSC-DV-001620"; // transmission confidentiality (cert validation)

    /// <summary>Scan a set of legacy source artifacts; returns all findings (the "before" set).</summary>
    public static IReadOnlyList<StigFinding> ScanLegacy(IReadOnlyList<SourceArtifact> sources)
    {
        var findings = new List<StigFinding>();
        foreach (SourceArtifact src in sources)
        {
            switch (src.Language)
            {
                case SourceLanguage.CSharp:
                    findings.AddRange(ScanCSharp(src.Path, src.Content));
                    break;
                case SourceLanguage.Sql:
                    findings.AddRange(ScanSql(src.Path, src.Content));
                    break;
                case SourceLanguage.JavaScript:
                    findings.AddRange(ScanJavaScript(src.Path, src.Content));
                    break;
            }
        }
        // Stable order for deterministic artifacts/tests.
        return findings
            .OrderBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.Location, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Build the "after" set: re-run the same detectors over the MODERN emitted files. Any
    /// legacy finding whose class is now ABSENT from the modern code is reported as
    /// RemediatedByTransform=true (carried into StigAfter); classes still present remain open.
    /// </summary>
    public static IReadOnlyList<StigFinding> ScanModernAndReconcile(
        IReadOnlyList<StigFinding> legacyFindings,
        IReadOnlyList<EmittedFile> modern)
    {
        // Findings still present in the modern code, keyed by RuleId.
        var modernFindings = new List<StigFinding>();
        foreach (EmittedFile f in modern)
        {
            switch (f.Language)
            {
                case SourceLanguage.CSharp:
                    modernFindings.AddRange(ScanCSharp(f.Path, f.Content));
                    break;
                case SourceLanguage.Sql:
                    modernFindings.AddRange(ScanSql(f.Path, f.Content));
                    break;
                case SourceLanguage.JavaScript:
                    modernFindings.AddRange(ScanJavaScript(f.Path, f.Content));
                    break;
            }
        }

        var stillPresentRuleIds = modernFindings.Select(f => f.RuleId).ToHashSet(StringComparer.Ordinal);

        // For each distinct legacy finding class, emit an "after" record flagged
        // RemediatedByTransform when the modern code no longer exhibits that class.
        var after = new List<StigFinding>();
        foreach (StigFinding lf in legacyFindings)
        {
            bool remediated = !stillPresentRuleIds.Contains(lf.RuleId);
            after.Add(lf with
            {
                Location = remediated ? "(remediated in modern code)" : lf.Location,
                RemediatedByTransform = remediated,
            });
        }
        return after
            .OrderBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.Location, StringComparer.Ordinal)
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // C# detectors (line-based; deterministic for the surrogate corpus)
    // ─────────────────────────────────────────────────────────────────────────────

    // A connection-string literal containing a server/data-source token (embedded config).
    private static readonly Regex ConnStringLiteral = new(
        "\"[^\"]*\\b(Server|Data\\s*Source|Initial\\s*Catalog|Integrated\\s+Security)\\s*=[^\"]*\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // An embedded password/secret in a connection string or assignment.
    private static readonly Regex EmbeddedSecret = new(
        "(Password|Pwd|AccountKey|SharedAccessKey|ApiKey|Token)\\s*=\\s*[\"']?[^\";'\\s]{3,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // TrustServerCertificate=true → server cert not validated (transmission confidentiality).
    private static readonly Regex TrustServerCert = new(
        "TrustServerCertificate\\s*=\\s*true", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // SqlCommand whose text is built by '+' string concatenation (dynamic-SQL construction).
    private static readonly Regex SqlCommandCtor = new(
        "new\\s+SqlCommand\\s*\\(", RegexOptions.Compiled);

    private static IEnumerable<StigFinding> ScanCSharp(string path, string content)
    {
        string[] lines = content.Replace("\r\n", "\n").Split('\n');

        // Track whether the file performs ADO.NET SQL command construction, and whether any
        // SqlCommand(...) text spans multiple "..." + "..." fragments (string-concatenated SQL).
        bool sawConcatSqlText = false;
        int concatSqlLine = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            string line = lines[i];

            // (1) Hardcoded connection string literal → APSC-DV-002400 / IA-5.
            if (ConnStringLiteral.IsMatch(line))
            {
                yield return new StigFinding
                {
                    RuleId = VID_HardcodedCreds,
                    Title = "Hardcoded database connection string (embedded authentication/configuration data)",
                    Severity = "CAT II",
                    Location = $"{path}:{lineNo}",
                    RemediatedByTransform = false,
                };
            }

            // (1b) Embedded secret material (password/key/token) in a literal.
            if (EmbeddedSecret.IsMatch(line))
            {
                yield return new StigFinding
                {
                    RuleId = VID_HardcodedCreds,
                    Title = "Embedded secret/authenticator in source literal",
                    Severity = "CAT I",
                    Location = $"{path}:{lineNo}",
                    RemediatedByTransform = false,
                };
            }

            // (2) Disabled TLS server-certificate validation → APSC-DV-001620 / SC-8.
            if (TrustServerCert.IsMatch(line))
            {
                yield return new StigFinding
                {
                    RuleId = VID_TlsCertValidation,
                    Title = "TLS server-certificate validation disabled (TrustServerCertificate=true)",
                    Severity = "CAT II",
                    Location = $"{path}:{lineNo}",
                    RemediatedByTransform = false,
                };
            }

            // (3) Detect a SqlCommand whose command text is assembled via "..." + "..."
            //     concatenation (dynamic-SQL construction pattern → APSC-DV-002500 / SI-10).
            if (SqlCommandCtor.IsMatch(line))
            {
                // Look at this line and the next few for string-literal '+' concatenation
                // forming the command text.
                for (int j = i; j < Math.Min(i + 6, lines.Length); j++)
                {
                    if (Regex.IsMatch(lines[j], "\"[^\"]*\"\\s*\\+") ||
                        Regex.IsMatch(lines[j], "\\+\\s*\"[^\"]*\""))
                    {
                        sawConcatSqlText = true;
                        if (concatSqlLine < 0) concatSqlLine = lineNo;
                        break;
                    }
                }
            }
        }

        if (sawConcatSqlText)
        {
            yield return new StigFinding
            {
                RuleId = VID_SqlInjection,
                Title = "SQL command text constructed by string concatenation in the publish path (dynamic-SQL / SQL-injection class)",
                Severity = "CAT I",
                Location = $"{path}:{concatSqlLine}",
                RemediatedByTransform = false,
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // SQL detectors (regex, per the spec)
    // ─────────────────────────────────────────────────────────────────────────────

    // Untrusted delimited-blob parsing with no validation (CHARINDEX/SUBSTRING/STRING_SPLIT
    // over an NVARCHAR(MAX) parameter) → missing input validation (APSC-DV-002560 / SI-10).
    private static readonly Regex DelimitedBlobParse = new(
        "STRING_SPLIT\\s*\\(|CHARINDEX\\s*\\(|SUBSTRING\\s*\\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Dynamic SQL execution (EXEC(@sql) / sp_executesql with a built string).
    private static readonly Regex DynamicExec = new(
        "EXEC\\s*\\(\\s*@|sp_executesql\\s+@", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static IEnumerable<StigFinding> ScanSql(string path, string content)
    {
        string[] lines = content.Replace("\r\n", "\n").Split('\n');

        bool sawBlobParse = false;
        int blobLine = -1;
        bool sawDynamicExec = false;
        int execLine = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            string line = lines[i];

            // Skip pure comment lines for the dynamic-exec check (avoid matching prose).
            string trimmed = line.TrimStart();
            bool isComment = trimmed.StartsWith("--", StringComparison.Ordinal);

            if (!isComment && DelimitedBlobParse.IsMatch(line))
            {
                if (!sawBlobParse) { sawBlobParse = true; blobLine = lineNo; }
            }
            if (!isComment && DynamicExec.IsMatch(line))
            {
                if (!sawDynamicExec) { sawDynamicExec = true; execLine = lineNo; }
            }
        }

        if (sawBlobParse)
        {
            yield return new StigFinding
            {
                RuleId = VID_InputValidation,
                Title = "Untrusted delimited blob parsed with no validation (CHARINDEX/SUBSTRING/STRING_SPLIT over an external parameter)",
                Severity = "CAT II",
                Location = $"{path}:{blobLine}",
                RemediatedByTransform = false,
            };
        }
        if (sawDynamicExec)
        {
            yield return new StigFinding
            {
                RuleId = VID_SqlInjection,
                Title = "Dynamic SQL executed from a constructed string (EXEC/sp_executesql)",
                Severity = "CAT I",
                Location = $"{path}:{execLine}",
                RemediatedByTransform = false,
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // JavaScript detectors (regex)
    // ─────────────────────────────────────────────────────────────────────────────

    // DOM HTML assembled by string concatenation then injected (.html()/innerHTML) with no
    // escaping → output-encoding / XSS class (APSC-DV-002490 / SI-10).
    private static readonly Regex HtmlConcat = new(
        "(['\"]<[a-zA-Z][^'\"]*['\"]\\s*\\+)|(\\.html\\s*\\(\\s*[a-zA-Z_$])|(innerHTML\\s*=)",
        RegexOptions.Compiled);

    private static IEnumerable<StigFinding> ScanJavaScript(string path, string content)
    {
        string[] lines = content.Replace("\r\n", "\n").Split('\n');

        bool sawHtmlConcat = false;
        int htmlLine = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();
            // Skip block/line comments so we match code, not the file's prose header.
            if (trimmed.StartsWith("*", StringComparison.Ordinal) ||
                trimmed.StartsWith("/*", StringComparison.Ordinal) ||
                trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (HtmlConcat.IsMatch(line))
            {
                if (!sawHtmlConcat) { sawHtmlConcat = true; htmlLine = i + 1; }
            }
        }

        if (sawHtmlConcat)
        {
            yield return new StigFinding
            {
                RuleId = VID_OutputEncoding,
                Title = "HTML built by string concatenation and injected without output encoding (DOM-based XSS class)",
                Severity = "CAT II",
                Location = $"{path}:{htmlLine}",
                RemediatedByTransform = false,
            };
        }
    }
}
