// FORGE EVOLVE for TMPC — Governance workstream (WS-H).
//
// GovernanceService is the concrete IGovernance: it records every pipeline action to the
// tamper-evident hashchain (the IGOM), exposes the current Merkle root, and evaluates the
// human-in-the-loop review gates — recording each gate decision back to the same chain.
//
// This is the only type other pipeline stages need to depend on; HashchainLedger,
// ReviewGateEvaluator and ProvenanceJson are the building blocks it composes.

using ForgeEvolve.Contracts;

namespace ForgeEvolve.Governance;

/// <summary>
/// Concrete <see cref="IGovernance"/>: tamper-evident provenance (SHA-256 hashchain + Merkle root)
/// plus recorded human-in-the-loop review gates. Deterministic; no secrets, no timestamps in hashes.
/// </summary>
public sealed class GovernanceService : IGovernance
{
    private readonly HashchainLedger _ledger;
    private readonly ReviewGateEvaluator _gates;

    /// <summary>Create a service over a fresh ledger.</summary>
    public GovernanceService() : this(new HashchainLedger()) { }

    /// <summary>Create a service over an existing ledger (e.g., to continue a loaded chain).</summary>
    public GovernanceService(HashchainLedger ledger)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _gates = new ReviewGateEvaluator(_ledger);
    }

    /// <summary>The underlying immutable governance object model (for serialization/inspection).</summary>
    public HashchainLedger Ledger => _ledger;

    /// <inheritdoc />
    public ProvenanceRecord Record(string action, string actor, string payloadJson)
        => _ledger.Append(action, actor, payloadJson);

    /// <inheritdoc />
    public string CurrentMerkleRoot() => _ledger.CurrentMerkleRoot();

    /// <inheritdoc />
    public ReviewGate Evaluate(string gateId, IReadOnlyDictionary<string, string> evidence)
        => _gates.Evaluate(gateId, evidence);

    /// <summary>Re-verify the entire hashchain (recompute hashes, confirm the links).</summary>
    public LedgerVerification Verify() => _ledger.Verify();

    /// <summary>
    /// Persist the ledger (records + Merkle root) to <paramref name="path"/>
    /// (default results/provenance.json). Returns the absolute path written.
    /// </summary>
    public string WriteProvenance(string path = "results/provenance.json")
        => ProvenanceJson.Write(_ledger, path);
}
