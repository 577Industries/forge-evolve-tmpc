// ─────────────────────────────────────────────────────────────────────────────
// CrossLanguageParser — lightweight, HEURISTIC parsing of the non-C# surrogate.
//
// PART OF: FORGE EVOLVE for TMPC, Discovery Engine (Stage 1, workstream WS-A).
//
// HONESTY NOTE: C# is parsed with Roslyn (a real AST). JavaScript, SQL and VB6 here are parsed
// with deliberately LIGHTWEIGHT regex/heuristic recognizers — enough to (a) confirm the file is
// the expected language and is structurally sane, (b) count it for the per-language ParseStats,
// and (c) emit a coarse ModuleNode (function / procedure / table) with a syntactic-CC estimate.
// These are NOT full grammars. In production FORGE EVOLVE uses real ANTLR grammars for VB6/SQL
// and an ESTree parser for JS; the heuristic recognizers below are the keyless/offline demo path.
// The pre-registered "≥95% parse" metric is measured on the C# (Roslyn) path only.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.RegularExpressions;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Discovery;

internal sealed class CrossLanguageResult
{
    public List<ModuleNode> Modules { get; } = new();
    public int FilesTotal { get; set; }
    public int FilesParsed { get; set; }
}

internal static partial class CrossLanguageParser
{
    // ── JavaScript ─────────────────────────────────────────────────────────────
    // Heuristic: a JS file "parses" if braces/parens balance and at least one function-like
    // construct is present. We extract top-level/nested `function name(...)` declarations as
    // Script-kind module nodes with a keyword-based CC estimate.
    [GeneratedRegex(@"function\s+([A-Za-z_$][\w$]*)\s*\(", RegexOptions.Compiled)]
    private static partial Regex JsFunctionRegex();

    public static CrossLanguageResult ParseJavaScript(IReadOnlyList<SourceArtifact> sources)
    {
        var r = new CrossLanguageResult { FilesTotal = sources.Count };
        foreach (var src in sources)
        {
            bool balanced = BracesBalanced(src.Content);
            var fns = JsFunctionRegex().Matches(src.Content);
            bool parsed = balanced && fns.Count > 0;
            if (parsed) r.FilesParsed++;

            foreach (Match m in fns)
            {
                int line = LineOf(src.Content, m.Index);
                r.Modules.Add(new ModuleNode
                {
                    Id = $"js:{Path.GetFileNameWithoutExtension(src.Path)}.{m.Groups[1].Value}",
                    DisplayName = m.Groups[1].Value,
                    Kind = ModuleKind.Script,
                    Language = SourceLanguage.JavaScript,
                    SourcePath = src.Path,
                    StartLine = line,
                    EndLine = line,
                    Complexity = EstimateComplexity(src.Content, KeywordsJsLike),
                });
            }
        }
        return r;
    }

    // ── SQL (T-SQL) ────────────────────────────────────────────────────────────
    // Heuristic: a SQL file "parses" if it contains at least one recognizable top-level DDL/DML
    // statement (CREATE TABLE / CREATE PROCEDURE / MERGE / INSERT / SELECT). CREATE TABLE -> Schema
    // node, CREATE PROCEDURE -> StoredProcedure node. CC estimated from control keywords
    // (IF/WHILE/CASE/CURSOR/MERGE/BEGIN...).
    [GeneratedRegex(@"CREATE\s+PROCEDURE\s+([\[\]\w\.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SqlProcRegex();

    [GeneratedRegex(@"CREATE\s+TABLE\s+([\[\]\w\.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SqlTableRegex();

    public static CrossLanguageResult ParseSql(IReadOnlyList<SourceArtifact> sources)
    {
        var r = new CrossLanguageResult { FilesTotal = sources.Count };
        foreach (var src in sources)
        {
            var procs = SqlProcRegex().Matches(src.Content);
            var tables = SqlTableRegex().Matches(src.Content);
            bool hasDml = Regex.IsMatch(src.Content, @"\b(INSERT|SELECT|UPDATE|DELETE|MERGE)\b",
                RegexOptions.IgnoreCase);
            bool parsed = procs.Count > 0 || tables.Count > 0 || hasDml;
            if (parsed) r.FilesParsed++;

            foreach (Match m in procs)
            {
                int line = LineOf(src.Content, m.Index);
                r.Modules.Add(new ModuleNode
                {
                    Id = $"sql:proc:{Clean(m.Groups[1].Value)}",
                    DisplayName = Clean(m.Groups[1].Value),
                    Kind = ModuleKind.StoredProcedure,
                    Language = SourceLanguage.Sql,
                    SourcePath = src.Path,
                    StartLine = line,
                    EndLine = LineOf(src.Content, src.Content.Length - 1),
                    Complexity = EstimateComplexity(src.Content, KeywordsSql),
                });
            }
            foreach (Match m in tables)
            {
                int line = LineOf(src.Content, m.Index);
                r.Modules.Add(new ModuleNode
                {
                    Id = $"sql:table:{Clean(m.Groups[1].Value)}",
                    DisplayName = Clean(m.Groups[1].Value),
                    Kind = ModuleKind.Schema,
                    Language = SourceLanguage.Sql,
                    SourcePath = src.Path,
                    StartLine = line,
                    EndLine = line,
                    Complexity = new ComplexityVector { CyclomaticComplexity = 1 },
                });
            }
        }
        return r;
    }

    // ── VB6 ─────────────────────────────────────────────────────────────────────
    // Heuristic: a .bas file "parses" if it declares a module (Attribute VB_Name) and at least one
    // Sub/Function. Each Public/Private Sub|Function becomes a Procedure node with a CC estimate
    // from VB control keywords (If/For/While/Do/Select Case/GoTo guards).
    [GeneratedRegex(@"(?:Public|Private|Friend)?\s*(?:Static\s+)?(?:Sub|Function)\s+([A-Za-z_]\w*)\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex VbProcRegex();

    public static CrossLanguageResult ParseVb6(IReadOnlyList<SourceArtifact> sources)
    {
        var r = new CrossLanguageResult { FilesTotal = sources.Count };
        foreach (var src in sources)
        {
            bool hasModule = src.Content.Contains("Attribute VB_Name", StringComparison.OrdinalIgnoreCase)
                             || src.Content.Contains("Option Explicit", StringComparison.OrdinalIgnoreCase);
            var procs = VbProcRegex().Matches(src.Content);
            bool parsed = hasModule && procs.Count > 0;
            if (parsed) r.FilesParsed++;

            string module = ExtractVbModuleName(src.Content) ?? Path.GetFileNameWithoutExtension(src.Path);
            foreach (Match m in procs)
            {
                int line = LineOf(src.Content, m.Index);
                r.Modules.Add(new ModuleNode
                {
                    Id = $"vb6:{module}.{m.Groups[1].Value}",
                    DisplayName = m.Groups[1].Value,
                    Kind = ModuleKind.Procedure,
                    Language = SourceLanguage.Vb6,
                    SourcePath = src.Path,
                    StartLine = line,
                    EndLine = line,
                    Complexity = EstimateComplexity(src.Content, KeywordsVb),
                });
            }
        }
        return r;
    }

    // ── Shared heuristics ────────────────────────────────────────────────────────

    private static readonly string[] KeywordsJsLike =
        { @"\bif\b", @"\bfor\b", @"\bwhile\b", @"\bcase\b", @"\bcatch\b", @"&&", @"\|\|", @"\?" };
    private static readonly string[] KeywordsSql =
        { @"\bIF\b", @"\bWHILE\b", @"\bCASE\b", @"\bCURSOR\b", @"\bMERGE\b", @"\bWHEN\b", @"\bBEGIN\b" };
    private static readonly string[] KeywordsVb =
        { @"\bIf\b", @"\bFor\b", @"\bWhile\b", @"\bDo\b", @"\bSelect\s+Case\b", @"\bGoTo\b", @"\bElseIf\b" };

    /// <summary>Crude syntactic CC estimate: 1 + total matches of control keywords across the file.</summary>
    private static ComplexityVector EstimateComplexity(string content, string[] keywordPatterns)
    {
        int decisions = 0;
        foreach (var p in keywordPatterns)
            decisions += Regex.Matches(content, p, RegexOptions.IgnoreCase).Count;
        int loc = content.Count(c => c == '\n') + 1;
        return new ComplexityVector
        {
            CyclomaticComplexity = 1 + decisions,
            LinesOfCode = loc,
            MaxNestingDepth = 0,
            FanIn = 0,
            FanOut = 0,
            CouplingCount = 0,
            TestCoverage = 0.0,
        };
    }

    private static bool BracesBalanced(string s)
    {
        int curly = 0, paren = 0;
        foreach (char c in s)
        {
            switch (c)
            {
                case '{': curly++; break;
                case '}': curly--; break;
                case '(': paren++; break;
                case ')': paren--; break;
            }
            if (curly < 0 || paren < 0) return false;
        }
        return curly == 0 && paren == 0;
    }

    private static string? ExtractVbModuleName(string content)
    {
        var m = Regex.Match(content, @"Attribute\s+VB_Name\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string Clean(string name) => name.Replace("[", "").Replace("]", "");

    private static int LineOf(string content, int index)
    {
        if (index < 0) index = 0;
        if (index >= content.Length) index = content.Length - 1;
        int line = 1;
        for (int i = 0; i < index && i < content.Length; i++)
            if (content[i] == '\n') line++;
        return line;
    }
}
