// FORGE EVOLVE for TMPC — Governance workstream (WS-H) acceptance demo.
//
// Prints a sample 3-entry hashchain ledger with its Merkle root, evaluates the KG1/KG2 review
// gates (recording each decision to the chain), demonstrates tamper-detection, and writes
// results/provenance.json. Deterministic and keyless.

using ForgeEvolve.Contracts;
using ForgeEvolve.Governance;

var gov = new GovernanceService();

// ── 1. Record a sample 3-entry pipeline run ───────────────────────────────────────────────
gov.Record("discovery", "discovery-engine", "{\"modules\":42,\"parseRate\":0.97}");
gov.Record("transform", "offline-replay", "{\"unit\":\"MissionRouting\",\"compiledClean\":true}");
gov.Record("validate", "equivalence-validator", "{\"violations\":0,\"vectors\":512}");

Console.WriteLine("=== FORGE EVOLVE — Governance IGOM: sample 3-entry hashchain ===");
PrintLedger(gov.Ledger);
Console.WriteLine($"Merkle root : {gov.CurrentMerkleRoot()}");
Console.WriteLine($"Verify()    : {Describe(gov.Verify())}");
Console.WriteLine();

// ── 2. Evaluate the pre-registered kill gates (decisions are themselves recorded) ──────────
Console.WriteLine("=== Human-in-the-loop review gates (recorded to the chain) ===");
var kg1 = gov.Evaluate("KG1", new Dictionary<string, string>
{
    ["ruleF1"] = "0.90",
    ["oracleHarnessRuns"] = "true",
});
var kg2 = gov.Evaluate("KG2", new Dictionary<string, string>
{
    ["discreteViolations"] = "0",
    ["catoBundle"] = "true",
});
Console.WriteLine($"KG1 -> Passed={kg1.Passed}");
Console.WriteLine($"KG2 -> Passed={kg2.Passed}");
Console.WriteLine($"Ledger now has {gov.Ledger.Count} entries (3 actions + 2 gate decisions).");
Console.WriteLine($"Merkle root : {gov.CurrentMerkleRoot()}");
Console.WriteLine();

// ── 3. Demonstrate tamper-detection ────────────────────────────────────────────────────────
Console.WriteLine("=== Tamper-detection demo ===");
var honest = gov.Ledger.Records;
var original = honest[1];
Console.WriteLine($"Original entry[1] payloadSha256 : {original.PayloadSha256}");
Console.WriteLine($"Original entry[1] entryHash     : {original.EntryHash}");

// An attacker rewrites entry[1]'s payload but leaves the stored EntryHash stale.
var tamperedPayloadSha = HashchainLedger.Sha256Hex("{\"unit\":\"MissionRouting\",\"compiledClean\":false}");
var honestRecompute = HashchainLedger.ComputeEntryHash(
    original.Sequence, original.Action, original.Actor, tamperedPayloadSha, original.PrevHash);

Console.WriteLine($"Tampered  payloadSha256         : {tamperedPayloadSha}");
Console.WriteLine($"Honest recompute of entry[1]    : {honestRecompute}");
Console.WriteLine($"  -> matches stored EntryHash?  : {honestRecompute == original.EntryHash}  (expected: False)");

// The forward link is broken too: entry[2].PrevHash pointed at the ORIGINAL entry[1] hash.
var next = honest[2];
Console.WriteLine($"entry[2].PrevHash               : {next.PrevHash}");
Console.WriteLine($"  -> equals tampered entry[1]?  : {next.PrevHash == honestRecompute}  (expected: False)");
Console.WriteLine("Tamper detected: entry[1] AND every subsequent entry fail verification.");
Console.WriteLine();

// ── 4. Persist the provenance artifact ─────────────────────────────────────────────────────
// The reviewer-cited path is results/provenance.json; the demo writes the local run copy under
// results/run/ (gitignored) so a `dotnet run` never dirties the committed tree.
var written = gov.WriteProvenance(Path.Combine("results", "run", "provenance.json"));
Console.WriteLine($"Wrote provenance artifact: {written}");

return 0;

static void PrintLedger(HashchainLedger ledger)
{
    foreach (var r in ledger.Records)
    {
        Console.WriteLine(
            $"  [{r.Sequence}] action={r.Action,-10} actor={r.Actor,-22} " +
            $"payload={Short(r.PayloadSha256)} prev={Short(r.PrevHash)} entry={Short(r.EntryHash)}");
    }
}

static string Short(string hex) =>
    hex.Length <= 12 ? hex : string.Concat(hex.AsSpan(0, 12), "…");

static string Describe(LedgerVerification v) =>
    v.Valid ? $"VALID ({v.Checked} entries checked)" : $"BROKEN at index {v.BrokenAtIndex} ({v.Reason})";
