// ─────────────────────────────────────────────────────────────────────────────
// SourceLoader — turns files on disk into SourceArtifacts (with SHA-256 provenance).
//
// PART OF: FORGE EVOLVE for TMPC, Discovery Engine (Stage 1, workstream WS-A).
//
// Maps file extensions to SourceLanguage and computes the lowercase-hex SHA-256 the contract's
// SourceArtifact.ContentSha256 requires. Used by tests (and any host) to feed the real surrogate
// files into the engine.
// ─────────────────────────────────────────────────────────────────────────────

using System.Security.Cryptography;
using System.Text;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Discovery;

public static class SourceLoader
{
    public static SourceLanguage LanguageFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => SourceLanguage.CSharp,
            ".js" => SourceLanguage.JavaScript,
            ".sql" => SourceLanguage.Sql,
            ".bas" or ".cls" or ".frm" => SourceLanguage.Vb6,
            ".cob" or ".cbl" => SourceLanguage.Cobol,
            ".f" or ".f90" or ".for" => SourceLanguage.Fortran,
            ".ada" or ".adb" or ".ads" => SourceLanguage.Ada,
            ".java" => SourceLanguage.Java,
            _ => SourceLanguage.Unknown,
        };

    public static SourceArtifact FromFile(string path)
    {
        string content = File.ReadAllText(path);
        return new SourceArtifact
        {
            Path = path,
            Language = LanguageFor(path),
            Content = content,
            ContentSha256 = Sha256Hex(content),
        };
    }

    public static SourceArtifact FromText(string path, SourceLanguage language, string content) =>
        new()
        {
            Path = path,
            Language = language,
            Content = content,
            ContentSha256 = Sha256Hex(content),
        };

    public static string Sha256Hex(string content)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
