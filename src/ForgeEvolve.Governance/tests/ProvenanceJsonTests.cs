// FORGE EVOLVE for TMPC — Governance workstream (WS-H) tests.
//
// Serialization tests: the results/provenance.json document round-trips through the Contracts
// ProvenanceRecord shape (so the Cyber/cATO overlay can read it back), and carries the Merkle root.

using ForgeEvolve.Governance;
using Xunit;

namespace ForgeEvolve.Governance.Tests;

public sealed class ProvenanceJsonTests
{
    private static HashchainLedger Sample()
    {
        var ledger = new HashchainLedger();
        ledger.Append("discovery", "discovery-engine", "{\"modules\":42}");
        ledger.Append("transform", "offline-replay", "{\"unit\":\"MissionRouting\"}");
        ledger.Append("validate", "equivalence-validator", "{\"violations\":0}");
        return ledger;
    }

    [Fact]
    public void Document_CarriesRecords_MerkleRoot_AndCount()
    {
        var ledger = Sample();
        var doc = ProvenanceJson.BuildDocument(ledger);

        Assert.Equal(3, doc.EntryCount);
        Assert.Equal(3, doc.Records.Count);
        Assert.Equal(ledger.CurrentMerkleRoot(), doc.MerkleRoot);
        Assert.Equal("forge-evolve/provenance/v1", doc.Schema);
    }

    [Fact]
    public void Serialize_RoundTrips_ThroughContractsProvenanceRecord()
    {
        var ledger = Sample();
        var json = ProvenanceJson.Serialize(ledger);

        var doc = ProvenanceJson.Deserialize(json);

        Assert.Equal(ledger.CurrentMerkleRoot(), doc.MerkleRoot);
        Assert.Equal(3, doc.Records.Count);
        for (var i = 0; i < doc.Records.Count; i++)
        {
            Assert.Equal(ledger.Records[i].Sequence, doc.Records[i].Sequence);
            Assert.Equal(ledger.Records[i].Action, doc.Records[i].Action);
            Assert.Equal(ledger.Records[i].Actor, doc.Records[i].Actor);
            Assert.Equal(ledger.Records[i].PayloadSha256, doc.Records[i].PayloadSha256);
            Assert.Equal(ledger.Records[i].PrevHash, doc.Records[i].PrevHash);
            Assert.Equal(ledger.Records[i].EntryHash, doc.Records[i].EntryHash);
        }
    }

    [Fact]
    public void Serialize_IsDeterministic_ForFixedSequence()
    {
        Assert.Equal(ProvenanceJson.Serialize(Sample()), ProvenanceJson.Serialize(Sample()));
    }

    [Fact]
    public void Write_CreatesFile_WithMerkleRootAndRecords()
    {
        var ledger = Sample();
        var dir = Path.Combine(Path.GetTempPath(), "forge-gov-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "results", "provenance.json");
        try
        {
            var written = ProvenanceJson.Write(ledger, path);
            Assert.True(File.Exists(written));

            var doc = ProvenanceJson.Deserialize(File.ReadAllText(written));
            Assert.Equal(ledger.CurrentMerkleRoot(), doc.MerkleRoot);
            Assert.Equal(3, doc.Records.Count);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
