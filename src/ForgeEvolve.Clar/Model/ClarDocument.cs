// FORGE EVOLVE for TMPC — CLAR document model.
//
// These classes serialize (System.Text.Json) to the exact shape of the FROZEN
// clar-spec/CLAR.schema.json. The schema sets "additionalProperties": false on every
// object, so we must NEVER emit a property the schema does not name. To guarantee that,
// optional members carry [JsonIgnore(WhenWritingNull)] and the enum-bearing "type"/
// "sourceLanguage" fields are emitted as strings using the schema's exact spellings.
//
// The four layers correspond to the schema's controlFlow / dataFlow / businessLogic /
// infrastructure arrays. The node "type" values come from the schema enums; the
// strongly-typed enums below keep the lifter honest and round-trip to those strings.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeEvolve.Clar.Model;

/// <summary>
/// A complete CLAR (Cross-Language Abstract Representation) JSON-LD document. Top-level
/// shape matches clar-spec/CLAR.schema.json (all eight properties are required).
/// </summary>
public sealed class ClarDocument
{
    /// <summary>JSON-LD context. The schema allows a string IRI or an inline object.</summary>
    [JsonPropertyName("@context")]
    public JsonElement Context { get; set; }

    [JsonPropertyName("clarVersion")]
    public string ClarVersion { get; set; } = ClarConstants.ClarVersion;

    [JsonPropertyName("sourceModuleId")]
    public string SourceModuleId { get; set; } = "";

    /// <summary>One of the schema's sourceLanguage enum strings (e.g. "CSharp").</summary>
    [JsonPropertyName("sourceLanguage")]
    public string SourceLanguage { get; set; } = "CSharp";

    [JsonPropertyName("controlFlow")]
    public List<ControlFlowNode> ControlFlow { get; set; } = new();

    [JsonPropertyName("dataFlow")]
    public List<DataFlowNode> DataFlow { get; set; } = new();

    [JsonPropertyName("businessLogic")]
    public List<BusinessLogicNode> BusinessLogic { get; set; } = new();

    [JsonPropertyName("infrastructure")]
    public List<InfrastructureNode> Infrastructure { get; set; } = new();
}

/// <summary>Control-flow layer node. "type" is one of the schema's controlFlowNode enums.</summary>
public sealed class ControlFlowNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter<ControlFlowType>))]
    public ControlFlowType Type { get; set; }

    [JsonPropertyName("children")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Children { get; set; }

    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }
}

/// <summary>
/// Data-flow layer node. "type" is one of the schema's dataFlowNode enums. The
/// <see cref="PrecisionConstrained"/> flag is the load-bearing field: when true (set for
/// coordinate / distance / TOT math, VB6 fixed-point, and SQL FLOAT-backed values) the
/// target generator MUST emit decimal/checked arithmetic instead of coercing to float.
/// </summary>
public sealed class DataFlowNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter<DataFlowType>))]
    public DataFlowType Type { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>Abstract type, e.g. "PrecisionConstrained", "Integer", "Boolean", "Text".</summary>
    [JsonPropertyName("clarType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClarType { get; set; }

    /// <summary>If true, the target generator MUST emit decimal/checked arithmetic.</summary>
    [JsonPropertyName("precisionConstrained")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PrecisionConstrained { get; set; }

    /// <summary>Original source type, e.g. "double", "FLOAT", "Currency".</summary>
    [JsonPropertyName("sourceType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceType { get; set; }
}

/// <summary>Business-logic layer node. "type" is one of the schema's businessLogicNode enums.</summary>
public sealed class BusinessLogicNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter<BusinessLogicType>))]
    public BusinessLogicType Type { get; set; }

    /// <summary>Id of the extracted BusinessRule (RDF) this node realizes.</summary>
    [JsonPropertyName("ruleRef")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleRef { get; set; }

    [JsonPropertyName("statement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Statement { get; set; }
}

/// <summary>Infrastructure layer node. "type" is one of the schema's infrastructureNode enums.</summary>
public sealed class InfrastructureNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter<InfrastructureType>))]
    public InfrastructureType Type { get; set; }

    /// <summary>Resource targeted, e.g. table name, endpoint, queue.</summary>
    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; set; }
}
