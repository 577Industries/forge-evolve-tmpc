// ─────────────────────────────────────────────────────────────────────────────
// CryptoInventory — weak-crypto and hardcoded-secret scan over all surrogate sources.
//
// PART OF: FORGE EVOLVE for TMPC, Discovery Engine (Stage 1, workstream WS-A).
//
// HONESTY NOTE: this reports ONLY what is actually present. The synthetic surrogate contains NO
// cryptographic primitives (no MD5/DES/SHA1/RSA/etc.), so the weak-crypto count is legitimately
// zero and we say so in the report notes. What IS present is a hardcoded connection string in
// LegacyConfig.PublishConnectionString plus an inline ADO.NET publish path — a credential /
// configuration-exposure finding, which we surface as a CryptoFinding with Family="Secret".
//
// Detection is regex/heuristic over source text. Algorithm patterns cover the common weak set so
// that, if a future surrogate revision introduces e.g. MD5, it is flagged automatically.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.RegularExpressions;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Discovery;

internal static class CryptoInventory
{
    private sealed record CryptoPattern(string Algorithm, string Family, string Pattern, bool IsWeak, bool QuantumVulnerable, string Severity);

    // Weak / legacy cryptographic primitives. None are expected in the current surrogate; present
    // so the scanner is real and future-proof.
    private static readonly CryptoPattern[] AlgorithmPatterns =
    {
        new("MD5",        "Hash",          @"\bMD5\b|MD5\.Create|MD5CryptoServiceProvider",        true,  false, "High"),
        new("SHA1",       "Hash",          @"\bSHA1\b|SHA1\.Create|SHA1Managed|SHA1CryptoServiceProvider", true, false, "High"),
        new("DES",        "Symmetric",     @"\bDES\b|DESCryptoServiceProvider|TripleDES|\b3DES\b", true,  false, "High"),
        new("RC2",        "Symmetric",     @"\bRC2\b|RC2CryptoServiceProvider",                    true,  false, "High"),
        new("RC4",        "Symmetric",     @"\bRC4\b|ARC4",                                        true,  false, "Critical"),
        new("RSA",        "AsymmetricRSA", @"\bRSA\b|RSACryptoServiceProvider|RSA\.Create",        false, true,  "Medium"),
        new("ECDSA",      "AsymmetricECC", @"\bECDsa\b|ECDsa\.Create|ECDH",                        false, true,  "Medium"),
        new("DSA",        "AsymmetricDSA", @"\bDSA\b|DSACryptoServiceProvider",                    true,  true,  "High"),
        new("MD4",        "Hash",          @"\bMD4\b",                                             true,  false, "Critical"),
    };

    // Hardcoded-secret / credential heuristics.
    private static readonly (string Id, string Pattern, string Severity, string Family)[] SecretPatterns =
    {
        ("connection-string", @"(Server|Data\s+Source)\s*=.*?(Integrated\s+Security|Password|Pwd)\s*=", "Medium", "Secret"),
        ("inline-password",   @"(?i)\b(password|pwd)\s*=\s*[""'][^""']+[""']",                            "High",   "Secret"),
        ("api-key",           @"(?i)\b(api[_-]?key|secret[_-]?key|access[_-]?token)\s*[:=]\s*[""'][^""']+[""']", "High", "Secret"),
    };

    public static List<CryptoFinding> Scan(IReadOnlyList<SourceArtifact> sources)
    {
        var findings = new List<CryptoFinding>();
        int counter = 0;

        foreach (var src in sources)
        {
            // 1) Cryptographic algorithm usages.
            foreach (var p in AlgorithmPatterns)
            {
                foreach (Match m in Regex.Matches(src.Content, p.Pattern))
                {
                    int line = LineOf(src.Content, m.Index);
                    findings.Add(new CryptoFinding
                    {
                        Id = $"crypto-{counter++:D3}",
                        Algorithm = p.Algorithm,
                        Family = p.Family,
                        Location = $"{src.Path}:{line}",
                        IsWeak = p.IsWeak,
                        QuantumVulnerable = p.QuantumVulnerable,
                        Severity = p.Severity,
                    });
                }
            }

            // 2) Hardcoded secrets / connection strings.
            foreach (var s in SecretPatterns)
            {
                foreach (Match m in Regex.Matches(src.Content, s.Pattern, RegexOptions.Singleline))
                {
                    int line = LineOf(src.Content, m.Index);
                    findings.Add(new CryptoFinding
                    {
                        Id = $"secret-{counter++:D3}",
                        Algorithm = s.Id,
                        Family = s.Family,
                        Location = $"{src.Path}:{line}",
                        IsWeak = true,           // a hardcoded credential is always a weakness
                        QuantumVulnerable = false,
                        Severity = s.Severity,
                    });
                }
            }
        }

        // De-duplicate findings that overlap (e.g. a connection string matched by two patterns on
        // the same line) — keep the highest-severity one per (location, family).
        return findings
            .GroupBy(f => (f.Location, f.Family))
            .Select(g => g.OrderByDescending(f => SeverityRank(f.Severity)).First())
            .OrderBy(f => f.Location, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>How many crypto-algorithm (non-secret) findings were produced — for the report note.</summary>
    public static int CountAlgorithmFindings(IEnumerable<CryptoFinding> findings) =>
        findings.Count(f => f.Family != "Secret");

    private static int SeverityRank(string s) => s switch
    {
        "Critical" => 4, "High" => 3, "Medium" => 2, "Low" => 1, _ => 0
    };

    private static int LineOf(string content, int index)
    {
        if (index < 0) index = 0;
        int line = 1;
        for (int i = 0; i < index && i < content.Length; i++)
            if (content[i] == '\n') line++;
        return line;
    }
}
