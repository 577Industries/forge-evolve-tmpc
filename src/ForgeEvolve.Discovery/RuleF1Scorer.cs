// ─────────────────────────────────────────────────────────────────────────────
// RuleF1Scorer — honest precision/recall/F1 of extracted rules vs. the gold TTL set.
//
// PART OF: FORGE EVOLVE for TMPC, Discovery Engine (Stage 1, workstream WS-A).
//
// MATCHING METHOD (documented for reviewers):
//   1. Parse surrogate/gold/business-rules.gold.ttl into (category, label, statement, expression)
//      tuples with a small, dependency-free Turtle reader (the gold file uses a fixed, simple
//      shape: `rule:X a fe:BusinessRule ; fe:category "..." ; ... .`).
//   2. For each gold rule and each extracted rule, build a normalized KEYWORD SET from its
//      statement + expression (lowercase, split on non-alphanumerics, drop stopwords, keep domain
//      tokens and numbers like "120", "1500", "mst", "ssn", "anti-meridian"->"anti","meridian").
//   3. A gold rule G and an extracted rule E MATCH iff:
//          same BusinessRuleCategory  AND  Jaccard(keywords(G), keywords(E)) >= THRESHOLD
//      where Jaccard = |A∩B| / |A∪B|. We use a one-to-one greedy assignment in descending Jaccard
//      so no extracted rule is credited against two gold rules (prevents F1 inflation).
//   4. Precision = TP / (TP + FP); Recall = TP / (TP + FN); F1 = 2PR/(P+R).
//
// This is a genuine semantic-overlap match, not a label lookup: an extracted rule earns a true
// positive only by independently describing the same concept in the same category. The threshold
// is set so paraphrases match but unrelated rules in the same category do NOT.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.RegularExpressions;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Discovery;

/// <summary>A rule loaded from the gold Turtle file.</summary>
public sealed record GoldRule(string Id, BusinessRuleCategory Category, string Label, string Statement, string Expression);

/// <summary>The computed F1 report, exposed so tests and the JSON output can cite it.</summary>
public sealed record RuleF1Report
{
    public required int GoldCount { get; init; }
    public required int ExtractedCount { get; init; }
    public required int TruePositives { get; init; }
    public required int FalsePositives { get; init; }
    public required int FalseNegatives { get; init; }
    public required double Precision { get; init; }
    public required double Recall { get; init; }
    public required double F1 { get; init; }
    public required double JaccardThreshold { get; init; }
    /// <summary>Per-match diagnostic lines (gold-id <-> extracted-id : jaccard).</summary>
    public required IReadOnlyList<string> Matches { get; init; }
    public required IReadOnlyList<string> UnmatchedGold { get; init; }
    public required IReadOnlyList<string> UnmatchedExtracted { get; init; }
}

public static class RuleF1Scorer
{
    public const double DefaultThreshold = 0.18;

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a","an","the","is","are","be","of","to","and","or","not","if","only","each","all","any",
        "must","may","that","this","so","on","in","at","with","without","before","after","every",
        "for","from","plus","its","it","as","by","than","into","then","else","when","where","which",
        "between","up","out","off","near","do","does","using","use","used","per","value","values",
        "check","copy","left","right","along","forall","abs","sum","round","sqrt","cos","sin",
    };

    /// <summary>Parse the gold Turtle file into structured rules with a minimal Turtle reader.</summary>
    public static List<GoldRule> ParseGoldTtl(string ttl)
    {
        var rules = new List<GoldRule>();
        // Split into statement blocks terminated by " ." at a line boundary. The gold file uses one
        // subject per block, with `;`-separated predicates. We tolerate comments (# ...).
        var noComments = string.Join("\n",
            ttl.Split('\n').Select(l => StripComment(l)));

        // Each block: starts with `rule:Name a fe:BusinessRule ;` ... ends with ` .`
        var blockRegex = new Regex(@"rule:(?<id>\w+)\s+a\s+fe:BusinessRule\s*;(?<body>.*?)\.\s*(?=rule:|\z)",
            RegexOptions.Singleline);
        foreach (Match block in blockRegex.Matches(noComments))
        {
            string id = block.Groups["id"].Value;
            string body = block.Groups["body"].Value;
            string category = ExtractLiteral(body, "fe:category") ?? "";
            string label = ExtractLiteral(body, "rdfs:label") ?? id;
            string statement = ExtractLiteral(body, "fe:statement") ?? "";
            string expression = ExtractLiteral(body, "fe:expression") ?? "";
            if (!Enum.TryParse<BusinessRuleCategory>(category, ignoreCase: true, out var cat))
                continue;
            rules.Add(new GoldRule(id, cat, label, statement, expression));
        }
        return rules;
    }

    public static RuleF1Report Score(
        IReadOnlyList<BusinessRule> extracted,
        IReadOnlyList<GoldRule> gold,
        double threshold = DefaultThreshold)
    {
        // Precompute keyword sets.
        var goldKw = gold.Select(g => (g, kw: Keywords(g.Label + " " + g.Statement + " " + g.Expression))).ToList();
        var extKw = extracted.Select(e => (e, kw: Keywords(e.Statement + " " + (e.Expression ?? "")))).ToList();

        // All candidate (gold, ext) pairs that share a category and clear the Jaccard threshold.
        var candidates = new List<(int gi, int ei, double j)>();
        for (int gi = 0; gi < goldKw.Count; gi++)
        for (int ei = 0; ei < extKw.Count; ei++)
        {
            if (goldKw[gi].g.Category != extKw[ei].e.Category) continue;
            double j = Jaccard(goldKw[gi].kw, extKw[ei].kw);
            if (j >= threshold) candidates.Add((gi, ei, j));
        }

        // Greedy one-to-one assignment, strongest overlap first.
        candidates.Sort((x, y) => y.j.CompareTo(x.j));
        var goldUsed = new bool[gold.Count];
        var extUsed = new bool[extracted.Count];
        var matchLines = new List<string>();
        int tp = 0;
        foreach (var (gi, ei, j) in candidates)
        {
            if (goldUsed[gi] || extUsed[ei]) continue;
            goldUsed[gi] = true;
            extUsed[ei] = true;
            tp++;
            matchLines.Add($"{gold[gi].Id} <-> {extracted[ei].Id} (jaccard={j:F3})");
        }

        int fp = extracted.Count - tp;
        int fn = gold.Count - tp;
        double precision = (tp + fp) == 0 ? 0 : (double)tp / (tp + fp);
        double recall = (tp + fn) == 0 ? 0 : (double)tp / (tp + fn);
        double f1 = (precision + recall) == 0 ? 0 : 2 * precision * recall / (precision + recall);

        var unmatchedGold = new List<string>();
        for (int i = 0; i < gold.Count; i++) if (!goldUsed[i]) unmatchedGold.Add(gold[i].Id);
        var unmatchedExt = new List<string>();
        for (int i = 0; i < extracted.Count; i++) if (!extUsed[i]) unmatchedExt.Add(extracted[i].Id);

        return new RuleF1Report
        {
            GoldCount = gold.Count,
            ExtractedCount = extracted.Count,
            TruePositives = tp,
            FalsePositives = fp,
            FalseNegatives = fn,
            Precision = precision,
            Recall = recall,
            F1 = f1,
            JaccardThreshold = threshold,
            Matches = matchLines,
            UnmatchedGold = unmatchedGold,
            UnmatchedExtracted = unmatchedExt,
        };
    }

    // ── Keyword normalization ────────────────────────────────────────────────────

    private static HashSet<string> Keywords(string text)
    {
        var tokens = Regex.Split(text.ToLowerInvariant(), @"[^a-z0-9]+")
            .Where(t => t.Length > 0)
            .Where(t => !Stopwords.Contains(t))
            .Where(t => t.Length > 1 || char.IsDigit(t[0])); // keep numbers, drop single letters
        return new HashSet<string>(tokens, StringComparer.Ordinal);
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 0;
        int inter = a.Count(b.Contains);
        int union = a.Count + b.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }

    // ── Minimal Turtle helpers ───────────────────────────────────────────────────

    private static string StripComment(string line)
    {
        // Remove a trailing # comment that is not inside a quoted literal.
        bool inQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') inQuote = !inQuote;
            else if (c == '#' && !inQuote) return line[..i];
        }
        return line;
    }

    private static string? ExtractLiteral(string body, string predicate)
    {
        var m = Regex.Match(body, Regex.Escape(predicate) + @"\s+""((?:[^""\\]|\\.)*)""", RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : null;
    }
}
