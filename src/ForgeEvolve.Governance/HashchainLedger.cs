// FORGE EVOLVE for TMPC — Governance workstream (WS-H).
//
// HashchainLedger is the Immutable Governance Object Model (IGOM): a tamper-evident,
// append-only SHA-256 hashchain with a binary Merkle root over all entries to date.
//
// The hashing shape mirrors 577's forge-os hashchain-audit
// (forge-os/hashchain-audit/src/{crypto,merkle}.ts):
//   * pipe-delimited field concatenation,
//   * the "GENESIS" sentinel for the first entry's PrevHash,
//   * lowercase-hex SHA-256 digests,
//   * a binary Merkle tree with odd-leaf duplication, internal node = SHA-256(left + right).
//
// The contract (ForgeEvolve.Contracts.IGovernance / ProvenanceRecord) fixes the leaf hash:
//   EntryHash = SHA-256(Sequence + Action + Actor + PayloadSha256 + PrevHash)
//
// Determinism: given the same ordered (action, actor, payloadJson) inputs, the entire chain,
// every EntryHash, and the Merkle root are bit-for-bit reproducible. No timestamps, no RNG,
// no secrets participate in the hash — this is what lets reviewers re-verify the run.

using System.Security.Cryptography;
using System.Text;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Governance;

/// <summary>
/// Append-only SHA-256 hashchain with a Merkle root over all entries (the IGOM).
/// Deterministic given the same ordered inputs; thread-safe for sequential appends.
/// </summary>
public sealed class HashchainLedger
{
    /// <summary>Sentinel used as the <c>PrevHash</c> of the first (genesis) entry.</summary>
    public const string Genesis = "GENESIS";

    private readonly List<ProvenanceRecord> _records = new();
    private readonly object _gate = new();

    /// <summary>All records in append order (the immutable chain). Defensive copy.</summary>
    public IReadOnlyList<ProvenanceRecord> Records
    {
        get { lock (_gate) { return _records.ToArray(); } }
    }

    /// <summary>Number of entries appended so far.</summary>
    public int Count
    {
        get { lock (_gate) { return _records.Count; } }
    }

    /// <summary>
    /// Append a tamper-evident entry for <paramref name="action"/> by <paramref name="actor"/>
    /// carrying <paramref name="payloadJson"/>. Returns the resulting <see cref="ProvenanceRecord"/>.
    /// </summary>
    public ProvenanceRecord Append(string action, string actor, string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(payloadJson);

        lock (_gate)
        {
            var sequence = _records.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var prevHash = _records.Count == 0 ? Genesis : _records[^1].EntryHash;
            var payloadSha256 = Sha256Hex(payloadJson);
            var entryHash = ComputeEntryHash(sequence, action, actor, payloadSha256, prevHash);

            var record = new ProvenanceRecord
            {
                Sequence = sequence,
                Action = action,
                Actor = actor,
                PayloadSha256 = payloadSha256,
                PrevHash = prevHash,
                EntryHash = entryHash,
            };
            _records.Add(record);
            return record;
        }
    }

    /// <summary>
    /// Merkle root (lowercase hex) over every entry's <c>EntryHash</c>, in order.
    /// Empty ledger → empty string. Single entry → that entry's hash.
    /// </summary>
    public string CurrentMerkleRoot()
    {
        lock (_gate)
        {
            var leaves = new string[_records.Count];
            for (var i = 0; i < _records.Count; i++)
            {
                leaves[i] = _records[i].EntryHash;
            }
            return ComputeMerkleRoot(leaves);
        }
    }

    /// <summary>
    /// Recompute every leaf hash from its stored fields and confirm each link points at the
    /// prior entry's hash. Returns the verification outcome (and the index that first breaks).
    /// Tampering with any payload invalidates that entry and — because PrevHash chains forward —
    /// every entry after it.
    /// </summary>
    public LedgerVerification Verify()
    {
        lock (_gate)
        {
            string expectedPrev = Genesis;
            for (var i = 0; i < _records.Count; i++)
            {
                var r = _records[i];
                var expectedSequence = i.ToString(System.Globalization.CultureInfo.InvariantCulture);

                if (!string.Equals(r.Sequence, expectedSequence, StringComparison.Ordinal))
                {
                    return LedgerVerification.Broken(i, "sequence_mismatch");
                }
                if (!string.Equals(r.PrevHash, expectedPrev, StringComparison.Ordinal))
                {
                    return LedgerVerification.Broken(i, "prevhash_mismatch");
                }

                var recomputed = ComputeEntryHash(r.Sequence, r.Action, r.Actor, r.PayloadSha256, r.PrevHash);
                if (!string.Equals(r.EntryHash, recomputed, StringComparison.Ordinal))
                {
                    return LedgerVerification.Broken(i, "entryhash_mismatch");
                }

                expectedPrev = r.EntryHash;
            }
            return LedgerVerification.Ok(_records.Count);
        }
    }

    /// <summary>
    /// Re-derive an entry's hash directly from a payload string (without trusting the stored
    /// <c>PayloadSha256</c>). Used by verification helpers/tests to prove that mutating a payload
    /// breaks the chain.
    /// </summary>
    public static string ComputeEntryHashFromPayload(
        string sequence, string action, string actor, string payloadJson, string prevHash)
        => ComputeEntryHash(sequence, action, actor, Sha256Hex(payloadJson), prevHash);

    // ── hashing primitives (mirrored from hashchain-audit) ──────────────────────────────────

    /// <summary>EntryHash = SHA-256(sequence | action | actor | payloadSha256 | prevHash).</summary>
    internal static string ComputeEntryHash(
        string sequence, string action, string actor, string payloadSha256, string prevHash)
    {
        var payload = string.Join("|", sequence, action, actor, payloadSha256, prevHash);
        return Sha256Hex(payload);
    }

    /// <summary>SHA-256 of a UTF-8 string, lowercase hex.</summary>
    internal static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Binary Merkle root over <paramref name="leaves"/>: odd layers duplicate the last node,
    /// internal node = SHA-256(left + right) over the lowercase-hex child strings.
    /// </summary>
    internal static string ComputeMerkleRoot(IReadOnlyList<string> leaves)
    {
        if (leaves.Count == 0) return string.Empty;
        if (leaves.Count == 1) return leaves[0];

        var layer = leaves.ToArray();
        while (layer.Length > 1)
        {
            var next = new string[(layer.Length + 1) / 2];
            for (int i = 0, j = 0; i < layer.Length; i += 2, j++)
            {
                var left = layer[i];
                var right = i + 1 < layer.Length ? layer[i + 1] : left; // duplicate if odd
                next[j] = Sha256Hex(left + right);
            }
            layer = next;
        }
        return layer[0];
    }
}

/// <summary>Outcome of re-verifying the hashchain: valid, plus where/why it first broke.</summary>
public sealed record LedgerVerification
{
    public required bool Valid { get; init; }
    /// <summary>Index of the first entry that fails verification, or -1 if valid.</summary>
    public required int BrokenAtIndex { get; init; }
    /// <summary>"sequence_mismatch" | "prevhash_mismatch" | "entryhash_mismatch", or null if valid.</summary>
    public string? Reason { get; init; }
    /// <summary>Number of entries checked (up to and including any break).</summary>
    public required int Checked { get; init; }

    public static LedgerVerification Ok(int checkedCount) =>
        new() { Valid = true, BrokenAtIndex = -1, Reason = null, Checked = checkedCount };

    public static LedgerVerification Broken(int index, string reason) =>
        new() { Valid = false, BrokenAtIndex = index, Reason = reason, Checked = index };
}
