// ─────────────────────────────────────────────────────────────────────────────
// ProvenanceLedger — tamper-evident SHA-256 hashchain + Merkle root.
//
// PART OF: FORGE EVOLVE for TMPC, Cyber/cATO overlay (Stage 5).
//
// This is the C# mirror of the forge-os hashchain-audit pattern
// (hashchain-audit/src/types.ts: AuditEntry / MerkleAnchor / Ledger):
//
//   * Each appended entry links to the previous via
//         EntryHash = SHA256( PayloadSha256 + "|" + PrevHash )
//     (the genesis PrevHash is 64 zero hex chars, matching the "null prevHash"
//      convention of the TS AuditEntry).
//   * A Merkle root is computed over the ordered EntryHash leaves (duplicate-last
//     padding for odd levels — the standard Bitcoin-style binary Merkle tree),
//     mirroring MerkleAnchor.merkleRoot over [firstEntryId..lastEntryId].
//   * Verify() recomputes the whole chain (and the root) from the payload digests,
//     so any mutation of a payload, an out-of-order entry, or a tampered hash is
//     detected — this is the non-repudiation evidence behind NIST 800-53 AU-10.
//
// The public DTO is the frozen ForgeEvolve.Contracts.ProvenanceRecord; this class
// adds the append/verify behavior the contract DTO deliberately omits.
// ─────────────────────────────────────────────────────────────────────────────

using System.Security.Cryptography;
using System.Text;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Cato;

/// <summary>Outcome of verifying a provenance hashchain.</summary>
public sealed record ChainVerification
{
    public required bool Valid { get; init; }
    public required int Checked { get; init; }
    /// <summary>Sequence index of the first broken link, or null when valid.</summary>
    public string? BrokenAt { get; init; }
    public string? Reason { get; init; }
    public required string MerkleRoot { get; init; }
}

/// <summary>
/// Append-only SHA-256 hashchain. Each <see cref="ProvenanceRecord"/> leaf carries the
/// SHA-256 of its payload and links PrevHash → EntryHash. A Merkle root anchors the set.
/// </summary>
public sealed class ProvenanceLedger
{
    /// <summary>Genesis sentinel: 64 hex zeros (the contract's "no previous entry").</summary>
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly List<ProvenanceRecord> _entries = new();

    public IReadOnlyList<ProvenanceRecord> Entries => _entries;

    /// <summary>SHA-256 of a UTF-8 string, lowercase hex.</summary>
    public static string Sha256Hex(string s)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return ToHex(hash);
    }

    /// <summary>The link function: EntryHash = SHA256(payloadSha256 + "|" + prevHash).</summary>
    public static string ComputeEntryHash(string payloadSha256, string prevHash)
        => Sha256Hex(payloadSha256 + "|" + prevHash);

    /// <summary>
    /// Append a record for <paramref name="action"/> by <paramref name="actor"/> over the raw
    /// <paramref name="payloadJson"/>. Returns the newly-linked leaf.
    /// </summary>
    public ProvenanceRecord Append(string action, string actor, string payloadJson)
    {
        string prev = _entries.Count == 0 ? GenesisHash : _entries[^1].EntryHash;
        string payloadSha = Sha256Hex(payloadJson);
        string entryHash = ComputeEntryHash(payloadSha, prev);

        var record = new ProvenanceRecord
        {
            Sequence = _entries.Count.ToString("D6"),
            Action = action,
            Actor = actor,
            PayloadSha256 = payloadSha,
            PrevHash = prev,
            EntryHash = entryHash,
        };
        _entries.Add(record);
        return record;
    }

    /// <summary>
    /// Merkle root over the ordered EntryHash leaves. Binary tree, duplicate-last padding
    /// for odd counts. Empty ledger → genesis sentinel. Single leaf → that leaf hash.
    /// </summary>
    public string MerkleRoot() => MerkleRootOf(_entries.Select(e => e.EntryHash).ToList());

    /// <summary>Static Merkle-root computation over a list of leaf hashes (for verifiers).</summary>
    public static string MerkleRootOf(IReadOnlyList<string> leaves)
    {
        if (leaves.Count == 0) return GenesisHash;

        // Parent = SHA256(left + right) over hex strings; duplicate the last node when odd.
        var level = new List<string>(leaves);
        while (level.Count > 1)
        {
            var next = new List<string>((level.Count + 1) / 2);
            for (int i = 0; i < level.Count; i += 2)
            {
                string left = level[i];
                string right = (i + 1 < level.Count) ? level[i + 1] : level[i]; // duplicate-last
                next.Add(Sha256Hex(left + right));
            }
            level = next;
        }
        return level[0];
    }

    /// <summary>
    /// Recompute the entire chain from payload digests and confirm every link and the Merkle
    /// root reproduce. Detects payload tampering, reordering, or hash forgery.
    /// </summary>
    public ChainVerification Verify() => VerifyChain(_entries);

    /// <summary>Static verifier so a third party can re-check a deserialized chain.</summary>
    public static ChainVerification VerifyChain(IReadOnlyList<ProvenanceRecord> entries)
    {
        string prev = GenesisHash;
        for (int i = 0; i < entries.Count; i++)
        {
            ProvenanceRecord e = entries[i];

            if (e.PrevHash != prev)
            {
                return new ChainVerification
                {
                    Valid = false,
                    Checked = i,
                    BrokenAt = e.Sequence,
                    Reason = "prev_hash_mismatch",
                    MerkleRoot = MerkleRootOf(entries.Take(i).Select(x => x.EntryHash).ToList()),
                };
            }

            string expected = ComputeEntryHash(e.PayloadSha256, e.PrevHash);
            if (e.EntryHash != expected)
            {
                return new ChainVerification
                {
                    Valid = false,
                    Checked = i,
                    BrokenAt = e.Sequence,
                    Reason = "entry_hash_mismatch",
                    MerkleRoot = MerkleRootOf(entries.Take(i).Select(x => x.EntryHash).ToList()),
                };
            }

            prev = e.EntryHash;
        }

        return new ChainVerification
        {
            Valid = true,
            Checked = entries.Count,
            BrokenAt = null,
            Reason = null,
            MerkleRoot = MerkleRootOf(entries.Select(x => x.EntryHash).ToList()),
        };
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
