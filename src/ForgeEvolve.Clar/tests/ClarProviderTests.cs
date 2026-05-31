// FORGE EVOLVE for TMPC — CLAR provider tests.
//
// Acceptance assertions:
//   * a lifted CLAR doc VALIDATES against clar-spec/CLAR.schema.json;
//   * the coordinate/distance/TOT dataFlow nodes are precisionConstrained=true;
//   * all four layers are populated;
//   * Validate() returns errors for a deliberately malformed doc.

using System.Text.Json;
using ForgeEvolve.Clar;
using ForgeEvolve.Clar.Model;
using Xunit;

namespace ForgeEvolve.Clar.Tests;

public sealed class ClarProviderTests
{
    private readonly ClarProvider _provider = new();

    private string LiftSurrogate()
        => _provider.Lift(SurrogateFixture.Module(), SurrogateFixture.Discovery());

    // ── Validation: lifted document conforms to the frozen schema ────────────────

    [Fact]
    public void LiftedDocument_ValidatesAgainstFrozenSchema()
    {
        string json = LiftSurrogate();
        IReadOnlyList<string> errors = _provider.Validate(json);
        Assert.True(errors.Count == 0,
            "Lifted CLAR doc must validate. Errors:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void LiftedDocument_DeclaresFrozenVersionAndLanguage()
    {
        using JsonDocument doc = JsonDocument.Parse(LiftSurrogate());
        JsonElement root = doc.RootElement;
        Assert.Equal("0.1.0", root.GetProperty("clarVersion").GetString());
        Assert.Equal("CSharp", root.GetProperty("sourceLanguage").GetString());
        Assert.Equal(SurrogateFixture.ModuleId, root.GetProperty("sourceModuleId").GetString());
        Assert.True(root.TryGetProperty("@context", out _), "@context (JSON-LD) must be present.");
    }

    // ── All four layers populated ────────────────────────────────────────────────

    [Fact]
    public void LiftedDocument_PopulatesAllFourLayers()
    {
        using JsonDocument doc = JsonDocument.Parse(LiftSurrogate());
        JsonElement root = doc.RootElement;

        Assert.True(root.GetProperty("controlFlow").GetArrayLength() > 0, "controlFlow empty");
        Assert.True(root.GetProperty("dataFlow").GetArrayLength() > 0, "dataFlow empty");
        Assert.True(root.GetProperty("businessLogic").GetArrayLength() > 0, "businessLogic empty");
        Assert.True(root.GetProperty("infrastructure").GetArrayLength() > 0, "infrastructure empty");
    }

    // ── Load-bearing feature: coordinate / distance / TOT are precision-constrained ─

    [Theory]
    [InlineData("latDeg")]
    [InlineData("lonDeg")]
    [InlineData("legDistanceNm")]
    [InlineData("totalDistanceNm")]
    [InlineData("travelSec")]
    [InlineData("estimatedTotEpochSec")]
    public void CoordinateDistanceAndTot_DataFlowNodes_ArePrecisionConstrained(string name)
    {
        ClarDocument model = _provider.LiftToModel(SurrogateFixture.Module(), SurrogateFixture.Discovery());
        DataFlowNode? node = model.DataFlow.FirstOrDefault(n => n.Name == name);

        Assert.NotNull(node);
        Assert.True(node!.PrecisionConstrained == true,
            $"dataFlow node '{name}' must be precisionConstrained=true.");
        Assert.Equal(ClarLifter.PrecisionConstrainedType, node.ClarType);
    }

    [Fact]
    public void CategoricalDataFlowNodes_AreNotPrecisionConstrained()
    {
        ClarDocument model = _provider.LiftToModel(SurrogateFixture.Module(), SurrogateFixture.Discovery());
        foreach (string name in new[] { "routeValid", "taskingGoNoGo", "platform", "variant" })
        {
            DataFlowNode? node = model.DataFlow.FirstOrDefault(n => n.Name == name);
            Assert.NotNull(node);
            Assert.False(node!.PrecisionConstrained == true,
                $"categorical node '{name}' must NOT be precision-constrained.");
        }
    }

    [Fact]
    public void PrecisionConstrained_SurvivesSerialization()
    {
        using JsonDocument doc = JsonDocument.Parse(LiftSurrogate());
        JsonElement dataFlow = doc.RootElement.GetProperty("dataFlow");

        int constrained = 0;
        foreach (JsonElement n in dataFlow.EnumerateArray())
        {
            if (n.TryGetProperty("precisionConstrained", out JsonElement pc) && pc.GetBoolean())
            {
                constrained++;
                Assert.Equal("PrecisionConstrained", n.GetProperty("clarType").GetString());
            }
        }
        Assert.True(constrained >= 6,
            $"expected >= 6 precision-constrained dataFlow nodes, found {constrained}.");
    }

    // ── Business-logic layer cites the gold rules ────────────────────────────────

    [Fact]
    public void BusinessLogic_CitesGoldRuleRefs()
    {
        ClarDocument model = _provider.LiftToModel(SurrogateFixture.Module(), SurrogateFixture.Discovery());
        var refs = model.BusinessLogic.Select(b => b.RuleRef).ToHashSet();

        Assert.Contains(ClarConstants.RuleRefs.MstSurfaceOnlyTasking, refs);
        Assert.Contains(ClarConstants.RuleRefs.GreatCircleLegDistance, refs);
        Assert.Contains(ClarConstants.RuleRefs.TurnRateLimit, refs);
        Assert.Contains(ClarConstants.RuleRefs.EstimatedTimeOnTarget, refs);
        Assert.Contains(ClarConstants.RuleRefs.MaxLegLength, refs);
    }

    [Fact]
    public void Infrastructure_IncludesSqlPublishAndConfig()
    {
        ClarDocument model = _provider.LiftToModel(SurrogateFixture.Module(), SurrogateFixture.Discovery());
        Assert.Contains(model.Infrastructure, n => n.Type == InfrastructureType.DB_QUERY);
        Assert.Contains(model.Infrastructure, n => n.Type == InfrastructureType.CONFIGURATION);
    }

    // ── Negative: Validate returns errors for malformed docs ─────────────────────

    [Fact]
    public void Validate_ReturnsErrors_ForMissingRequiredLayers()
    {
        // Missing dataFlow/businessLogic/infrastructure (required) and a bogus version.
        const string malformed = """
        {
          "@context": "https://example.org/ctx",
          "clarVersion": "9.9.9",
          "sourceModuleId": "X",
          "sourceLanguage": "CSharp",
          "controlFlow": []
        }
        """;
        IReadOnlyList<string> errors = _provider.Validate(malformed);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validate_ReturnsErrors_ForBadEnumValue()
    {
        // dataFlow node uses a "type" that is not in the schema enum.
        const string badEnum = """
        {
          "@context": "https://example.org/ctx",
          "clarVersion": "0.1.0",
          "sourceModuleId": "X",
          "sourceLanguage": "CSharp",
          "controlFlow": [],
          "dataFlow": [ { "id": "d1", "type": "NOT_A_REAL_TYPE" } ],
          "businessLogic": [],
          "infrastructure": []
        }
        """;
        IReadOnlyList<string> errors = _provider.Validate(badEnum);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validate_ReturnsErrors_ForAdditionalProperty()
    {
        // additionalProperties:false at the top level => bogusField is an error.
        const string extra = """
        {
          "@context": "https://example.org/ctx",
          "clarVersion": "0.1.0",
          "sourceModuleId": "X",
          "sourceLanguage": "CSharp",
          "controlFlow": [],
          "dataFlow": [],
          "businessLogic": [],
          "infrastructure": [],
          "bogusField": 1
        }
        """;
        IReadOnlyList<string> errors = _provider.Validate(extra);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validate_ReturnsError_ForMalformedJson()
    {
        IReadOnlyList<string> errors = _provider.Validate("{ this is not json ");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validate_ReturnsEmpty_ForMinimalValidDocument()
    {
        const string minimal = """
        {
          "@context": "https://example.org/ctx",
          "clarVersion": "0.1.0",
          "sourceModuleId": "X",
          "sourceLanguage": "Vb6",
          "controlFlow": [],
          "dataFlow": [],
          "businessLogic": [],
          "infrastructure": []
        }
        """;
        Assert.Empty(_provider.Validate(minimal));
    }

    // ── File helper writes results/clar/<module>.clar.jsonld ─────────────────────

    [Fact]
    public void LiftToFile_WritesValidatingDocumentToResultsClar()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "fe-clar-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = _provider.LiftToFile(SurrogateFixture.Module(), SurrogateFixture.Discovery(), tmp);
            Assert.True(File.Exists(path), "expected the CLAR file to be written.");
            Assert.EndsWith(".clar.jsonld", path);
            Assert.Contains(Path.Combine("clar"), path);

            string json = File.ReadAllText(path);
            Assert.Empty(_provider.Validate(json));
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        }
    }
}
