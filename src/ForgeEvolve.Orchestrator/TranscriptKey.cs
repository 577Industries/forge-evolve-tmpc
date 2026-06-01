using System.Security.Cryptography;
using System.Text;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Orchestrator;

/// <summary>
/// Computes the deterministic lookup key for a recorded transcript.
///
/// The key is the lowercase-hex SHA-256 of the canonical string
/// <c>{Unit.Id}|{SourceLanguage}|{TargetStack}</c>. This is intentionally
/// stable and reviewer-reproducible: given the same migration unit, source
/// language and target stack, the offline orchestrator always resolves to the
/// same recorded <see cref="TransformResult"/> — which is what makes
/// <c>make verify</c> produce byte-identical evidence across runs.
/// </summary>
public static class TranscriptKey
{
    /// <summary>Compute the transcript key for a transform task.</summary>
    public static string For(TransformTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return For(task.Unit.Id, task.SourceLanguage, task.TargetStack);
    }

    /// <summary>Compute the transcript key from its three canonical components.</summary>
    public static string For(string unitId, SourceLanguage sourceLanguage, string targetStack)
    {
        ArgumentNullException.ThrowIfNull(unitId);
        ArgumentNullException.ThrowIfNull(targetStack);

        // SourceLanguage is serialized by its enum NAME (matching the JsonStringEnumConverter
        // used on the contract) so the key is human-auditable and stable across .NET versions.
        var canonical = $"{unitId}|{sourceLanguage}|{targetStack}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Lowercase-hex SHA-256 of an arbitrary string (used for PromptSha256 provenance).</summary>
    public static string Sha256Hex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
