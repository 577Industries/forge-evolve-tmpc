using System.Text.Json;
using ForgeEvolve.Contracts;
using Xunit;

namespace ForgeEvolve.Cato.Tests;

public class StigAnalyzerTests
{
    [Fact]
    public void LegacyScan_Finds_SqlInjection_HardcodedCreds_And_MissingValidation()
    {
        IReadOnlyList<StigFinding> before = StigAnalyzer.ScanLegacy(SurrogateFixture.Legacy());

        // SQL-injection class: string-concatenated SQL command construction in the publish path.
        Assert.Contains(before, f => f.RuleId == StigAnalyzer.VID_SqlInjection);

        // Hardcoded credential / connection string literal.
        Assert.Contains(before, f => f.RuleId == StigAnalyzer.VID_HardcodedCreds);

        // Missing input validation: untrusted delimited blob parsed with no validation (SQL).
        Assert.Contains(before, f => f.RuleId == StigAnalyzer.VID_InputValidation);

        // Every finding must point at a real location in the surrogate.
        Assert.All(before, f => Assert.False(string.IsNullOrWhiteSpace(f.Location)));
        Assert.All(before, f => Assert.False(f.RemediatedByTransform)); // "before" set is unremediated
    }

    [Fact]
    public void HardcodedConnString_Points_At_MissionProcessor()
    {
        IReadOnlyList<StigFinding> before = StigAnalyzer.ScanLegacy(SurrogateFixture.Legacy());
        StigFinding creds = Assert.Single(
            before, f => f.RuleId == StigAnalyzer.VID_HardcodedCreds &&
                         f.Title.Contains("connection string", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("MissionProcessor.cs", creds.Location);
    }

    [Fact]
    public void CleanModern_Reconciles_LegacyFindings_As_Remediated()
    {
        IReadOnlyList<StigFinding> before = StigAnalyzer.ScanLegacy(SurrogateFixture.Legacy());
        IReadOnlyList<StigFinding> after =
            StigAnalyzer.ScanModernAndReconcile(before, SurrogateFixture.CleanModern());

        // The three required classes must reconcile to RemediatedByTransform=true.
        foreach (string ruleId in new[]
                 {
                     StigAnalyzer.VID_SqlInjection,
                     StigAnalyzer.VID_HardcodedCreds,
                     StigAnalyzer.VID_InputValidation,
                 })
        {
            Assert.All(after.Where(f => f.RuleId == ruleId),
                f => Assert.True(f.RemediatedByTransform, $"{ruleId} should be remediated"));
        }

        // After-set count equals before-set count (one reconciled record per finding).
        Assert.Equal(before.Count, after.Count);
    }

    [Fact]
    public void ModernCode_StillExhibitingDefect_Is_NotRemediated()
    {
        IReadOnlyList<StigFinding> before = StigAnalyzer.ScanLegacy(SurrogateFixture.Legacy());

        // A "modern" file that STILL embeds a connection string — must remain unremediated.
        var stillBad = new[]
        {
            new EmittedFile
            {
                Path = "modern/Bad.cs",
                Language = SourceLanguage.CSharp,
                Content = "var cs = \"Server=(localdb)\\\\X;Database=Y;Integrated Security=true;\";",
            },
        };
        IReadOnlyList<StigFinding> after = StigAnalyzer.ScanModernAndReconcile(before, stillBad);
        Assert.All(after.Where(f => f.RuleId == StigAnalyzer.VID_HardcodedCreds),
            f => Assert.False(f.RemediatedByTransform));
    }
}

public class ProvenanceLedgerTests
{
    [Fact]
    public void Hashchain_Verifies_And_Recomputes_EntryHash_From_Prev_Plus_Payload()
    {
        var ledger = new ProvenanceLedger();
        ledger.Append("a", "tester", "{\"x\":1}");
        ledger.Append("b", "tester", "{\"x\":2}");
        ledger.Append("c", "tester", "{\"x\":3}");

        // Independent recomputation: each EntryHash = SHA256(PayloadSha256 + "|" + PrevHash).
        string prev = ProvenanceLedger.GenesisHash;
        foreach (ProvenanceRecord e in ledger.Entries)
        {
            string expected = ProvenanceLedger.ComputeEntryHash(e.PayloadSha256, prev);
            Assert.Equal(e.PrevHash, prev);
            Assert.Equal(expected, e.EntryHash);
            prev = e.EntryHash;
        }

        ChainVerification v = ledger.Verify();
        Assert.True(v.Valid);
        Assert.Null(v.BrokenAt);
        Assert.Equal(ledger.Entries.Count, v.Checked);
        Assert.Equal(ledger.MerkleRoot(), v.MerkleRoot);
    }

    [Fact]
    public void Tampering_With_A_Payload_Breaks_The_Chain()
    {
        var ledger = new ProvenanceLedger();
        ledger.Append("a", "tester", "{\"x\":1}");
        ledger.Append("b", "tester", "{\"x\":2}");
        ledger.Append("c", "tester", "{\"x\":3}");

        // Forge the middle entry's payload digest; EntryHash no longer recomputes.
        var entries = ledger.Entries.ToList();
        entries[1] = entries[1] with { PayloadSha256 = ProvenanceLedger.Sha256Hex("TAMPERED") };

        ChainVerification v = ProvenanceLedger.VerifyChain(entries);
        Assert.False(v.Valid);
        Assert.Equal(entries[1].Sequence, v.BrokenAt);
    }

    [Fact]
    public void MerkleRoot_Is_Deterministic_And_Order_Sensitive()
    {
        var a = new ProvenanceLedger();
        a.Append("x", "t", "1"); a.Append("y", "t", "2");
        var b = new ProvenanceLedger();
        b.Append("x", "t", "1"); b.Append("y", "t", "2");
        Assert.Equal(a.MerkleRoot(), b.MerkleRoot());

        var c = new ProvenanceLedger();
        c.Append("y", "t", "2"); c.Append("x", "t", "1"); // reversed
        Assert.NotEqual(a.MerkleRoot(), c.MerkleRoot());
    }
}

public class SbomBuilderTests
{
    [Fact]
    public void Sbom_Is_Valid_CycloneDx_15_Json()
    {
        string json = SbomBuilder.Build(
            "tmpc-surrogate-mds", "1.0.0",
            new[] { new SbomComponent("Microsoft.Data.SqlClient", "5.2.0") },
            serialSeed: "seed");

        Assert.True(SbomBuilder.IsValidCycloneDx(json));

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement r = doc.RootElement;
        Assert.Equal("CycloneDX", r.GetProperty("bomFormat").GetString());
        Assert.Equal("1.5", r.GetProperty("specVersion").GetString());
        Assert.Equal(JsonValueKind.Array, r.GetProperty("components").ValueKind);
        Assert.True(r.GetProperty("components").GetArrayLength() >= 1);
        Assert.StartsWith("urn:uuid:", r.GetProperty("serialNumber").GetString());
    }
}

public class CyberOverlayEndToEndTests
{
    [Fact]
    public void Generate_Produces_All_Artifacts_And_Wellformed_Bundle()
    {
        var overlay = new CyberOverlay();
        string outDir = Path.Combine(Path.GetTempPath(), "cato-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            CatoArtifacts art = overlay.Generate(
                SurrogateFixture.Legacy(),
                SurrogateFixture.CleanModern(),
                SurrogateFixture.Discovery(),
                outDir);

            // STIG before has the three required findings; after has them all remediated.
            Assert.Contains(art.StigBefore, f => f.RuleId == StigAnalyzer.VID_SqlInjection);
            Assert.Contains(art.StigBefore, f => f.RuleId == StigAnalyzer.VID_HardcodedCreds);
            Assert.Contains(art.StigBefore, f => f.RuleId == StigAnalyzer.VID_InputValidation);
            Assert.All(art.StigAfter, f => Assert.True(f.RemediatedByTransform));

            // Control map covers the required control IDs.
            var controlIds = art.ControlMap.Select(c => c.ControlId).ToHashSet();
            foreach (string expected in new[] { "SI-10", "IA-5", "AU-10", "SA-11", "SA-15" })
                Assert.Contains(expected, controlIds);

            // Merkle root present and matches the written provenance.json.
            Assert.False(string.IsNullOrWhiteSpace(art.ProvenanceMerkleRoot));
            string provJson = File.ReadAllText(Path.Combine(outDir, "provenance.json"));
            using (JsonDocument prov = JsonDocument.Parse(provJson))
            {
                Assert.Equal(art.ProvenanceMerkleRoot, prov.RootElement.GetProperty("merkleRoot").GetString());
                // Re-verify the written chain end-to-end.
                List<ProvenanceRecord> entries = prov.RootElement.GetProperty("entries")
                    .EnumerateArray()
                    .Select(e => new ProvenanceRecord
                    {
                        Sequence = e.GetProperty("sequence").GetString()!,
                        Action = e.GetProperty("action").GetString()!,
                        Actor = e.GetProperty("actor").GetString()!,
                        PayloadSha256 = e.GetProperty("payloadSha256").GetString()!,
                        PrevHash = e.GetProperty("prevHash").GetString()!,
                        EntryHash = e.GetProperty("entryHash").GetString()!,
                    }).ToList();
                ChainVerification v = ProvenanceLedger.VerifyChain(entries);
                Assert.True(v.Valid);
                Assert.Equal(art.ProvenanceMerkleRoot, v.MerkleRoot);
            }

            // SBOM written and valid CycloneDX.
            Assert.True(File.Exists(art.SbomPath));
            Assert.True(SbomBuilder.IsValidCycloneDx(File.ReadAllText(art.SbomPath)));

            // POA&M: the latent D1/D2/D3 defects are present, Open, ECP-recommended (not auto-fixed).
            Assert.True(File.Exists(Path.Combine(outDir, "poam.csv")));
            var computational = art.Poam.Where(p => p.Id.StartsWith("POAM-C", StringComparison.Ordinal)).ToList();
            Assert.Equal(3, computational.Count);
            Assert.All(computational, p => Assert.Equal("Open", p.Status));
            Assert.All(computational, p => Assert.Equal("ECP-recommended", p.ScheduledCompletion));
            Assert.All(computational, p => Assert.Contains("ECP", p.Weakness));

            // All expected files exist.
            foreach (string rel in new[]
                     {
                         "cato/stig-before.json", "cato/stig-after.json",
                         "cato/control-map.yaml", "cato/control-map.json",
                         "sbom.cdx.json", "provenance.json", "poam.csv", "poam.json",
                     })
                Assert.True(File.Exists(Path.Combine(outDir, rel)), $"missing artifact: {rel}");
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Generate_Is_Deterministic_Same_Inputs_Same_MerkleRoot()
    {
        var overlay = new CyberOverlay();
        string d1 = Path.Combine(Path.GetTempPath(), "cato-det1-" + Guid.NewGuid().ToString("N"));
        string d2 = Path.Combine(Path.GetTempPath(), "cato-det2-" + Guid.NewGuid().ToString("N"));
        try
        {
            CatoArtifacts a1 = overlay.Generate(SurrogateFixture.Legacy(), SurrogateFixture.CleanModern(),
                SurrogateFixture.Discovery(), d1);
            CatoArtifacts a2 = overlay.Generate(SurrogateFixture.Legacy(), SurrogateFixture.CleanModern(),
                SurrogateFixture.Discovery(), d2);
            Assert.Equal(a1.ProvenanceMerkleRoot, a2.ProvenanceMerkleRoot);
        }
        finally
        {
            if (Directory.Exists(d1)) Directory.Delete(d1, recursive: true);
            if (Directory.Exists(d2)) Directory.Delete(d2, recursive: true);
        }
    }
}
