// FORGE EVOLVE for TMPC — frozen data contracts (DTOs).
//
// These records are the language-neutral payloads that flow between pipeline stages and are
// serialized to the JSON artifacts under results/ that the proposal cites. Treat this file as a
// FROZEN CONTRACT: additive changes are fine; breaking changes require re-freezing in Phase 0.
//
// Design rules:
//  * Records are immutable (init-only) and JSON-serializable with System.Text.Json defaults.
//  * No engine logic here — DTOs and enums only.
//  * Every artifact carries enough provenance to be reviewer-verifiable (hashes, source refs).

using System.Text.Json.Serialization;

namespace ForgeEvolve.Contracts;

// ─────────────────────────────────────────────────────────────────────────────
// Source model
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A source language the pipeline can ingest. The TMPC-relevant set is first.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceLanguage
{
    CSharp,
    JavaScript,
    Sql,
    Vb6,
    // Already-validated FORGE EVOLVE source languages (supported, not the TMPC focus):
    Cobol,
    Fortran,
    Ada,
    Java,
    Unknown
}

/// <summary>A single source file presented to the Discovery Engine.</summary>
public sealed record SourceArtifact
{
    public required string Path { get; init; }
    public required SourceLanguage Language { get; init; }
    public required string Content { get; init; }
    /// <summary>SHA-256 of <see cref="Content"/> (lowercase hex). Provenance anchor.</summary>
    public required string ContentSha256 { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Discovery model
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Quantitative debt/complexity signal for one module (class/file/procedure).</summary>
public sealed record ComplexityVector
{
    public int CyclomaticComplexity { get; init; }
    public int LinesOfCode { get; init; }
    public int MaxNestingDepth { get; init; }
    public int FanIn { get; init; }
    public int FanOut { get; init; }
    public double HalsteadVolume { get; init; }
    /// <summary>Number of distinct external/data couplings (e.g., direct DB calls, globals).</summary>
    public int CouplingCount { get; init; }
    /// <summary>0.0–1.0; 0 = no tests cover this module.</summary>
    public double TestCoverage { get; init; }
}

/// <summary>The kind of code element a <see cref="ModuleNode"/> represents.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModuleKind { Solution, Project, Namespace, Type, Method, Procedure, Script, Schema, StoredProcedure }

/// <summary>A discovered unit of code with its location, language, and complexity.</summary>
public sealed record ModuleNode
{
    /// <summary>Stable identifier, e.g. "MissionRouting.MissionProcessor.ValidateRoute".</summary>
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required ModuleKind Kind { get; init; }
    public required SourceLanguage Language { get; init; }
    public required string SourcePath { get; init; }
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public ComplexityVector Complexity { get; init; } = new();
    /// <summary>Composite modernization-risk score in [0,1]; populated by the Planner.</summary>
    public double RiskScore { get; init; }
}

/// <summary>The kind of dependency between two modules.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DependencyKind { Calls, Inherits, References, DataAccess, CrossLanguageApi, Configuration }

public sealed record DependencyEdge
{
    public required string FromId { get; init; }
    public required string ToId { get; init; }
    public required DependencyKind Kind { get; init; }
    public int Weight { get; init; } = 1;
}

/// <summary>A strongly-connected component (tight coupling cluster) found by Tarjan's algorithm.</summary>
public sealed record StronglyConnectedComponent
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> MemberIds { get; init; }
}

/// <summary>The classification of an extracted business rule.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BusinessRuleCategory { Calculation, Validation, Routing, Constraint }

/// <summary>An implicit mission/business rule recovered from legacy code (AST + LLM majority vote).</summary>
public sealed record BusinessRule
{
    public required string Id { get; init; }
    public required BusinessRuleCategory Category { get; init; }
    /// <summary>Plain-language statement of the rule.</summary>
    public required string Statement { get; init; }
    /// <summary>Optional formal/pseudo expression, e.g. "tot_ticks = epoch_ticks + leg_seconds * 10_000_000".</summary>
    public string? Expression { get; init; }
    /// <summary>Module ids the rule was extracted from.</summary>
    public required IReadOnlyList<string> SourceRefs { get; init; }
    /// <summary>Ensemble-agreement confidence in [0,1].</summary>
    public double Confidence { get; init; }
}

/// <summary>A cryptographic usage found during static analysis, with quantum-vulnerability triage.</summary>
public sealed record CryptoFinding
{
    public required string Id { get; init; }
    public required string Algorithm { get; init; }
    public required string Family { get; init; } // e.g. "Symmetric", "Hash", "AsymmetricRSA"
    public required string Location { get; init; } // "path:line"
    public bool IsWeak { get; init; }
    public bool QuantumVulnerable { get; init; }
    public string Severity { get; init; } = "Medium"; // Low|Medium|High|Critical
}

/// <summary>The full output of the Discovery Engine. Serialized to results/discovery-report.json.</summary>
public sealed record DiscoveryReport
{
    public required IReadOnlyList<ModuleNode> Modules { get; init; }
    public required IReadOnlyList<DependencyEdge> Edges { get; init; }
    public required IReadOnlyList<StronglyConnectedComponent> Sccs { get; init; }
    public required IReadOnlyList<BusinessRule> BusinessRules { get; init; }
    public required IReadOnlyList<CryptoFinding> CryptoFindings { get; init; }
    /// <summary>Languages and per-language parsed/total file counts, for the "≥95% parsed" exit metric.</summary>
    public required IReadOnlyDictionary<string, ParseStats> ParseStatsByLanguage { get; init; }
}

public sealed record ParseStats
{
    public int FilesParsed { get; init; }
    public int FilesTotal { get; init; }
    public double ParseRate => FilesTotal == 0 ? 0 : (double)FilesParsed / FilesTotal;
}

// ─────────────────────────────────────────────────────────────────────────────
// Migration planning model
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A proposed migration unit (candidate microservice) with members and an API contract.</summary>
public sealed record MigrationUnit
{
    public required string Id { get; init; }
    public required string ProposedServiceName { get; init; }
    public required IReadOnlyList<string> MemberModuleIds { get; init; }
    public double AggregateRiskScore { get; init; }
    /// <summary>Names of CLAR-derived API operations exposed at this boundary.</summary>
    public IReadOnlyList<string> ApiOperations { get; init; } = Array.Empty<string>();
}

/// <summary>The risk-scored, topologically-sorted migration plan. results/migration-plan.json.</summary>
public sealed record MigrationPlan
{
    public required IReadOnlyList<MigrationUnit> Units { get; init; }
    /// <summary>Recommended migration order (unit ids), lowest-risk / least-dependent first.</summary>
    public required IReadOnlyList<string> OrderedUnitIds { get; init; }
    /// <summary>Inter-unit dependency edges (the strangler-fig seams).</summary>
    public required IReadOnlyList<DependencyEdge> UnitEdges { get; init; }
    public string? MermaidDiagram { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Transformation model
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Where a transform task should be routed and how it should run.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrchestratorMode { Offline, Local, Cloud }

/// <summary>A unit of work handed to the Tool Orchestrator / Transformation Engine.</summary>
public sealed record TransformTask
{
    public required string TaskId { get; init; }
    public required MigrationUnit Unit { get; init; }
    public required string ClarDocumentJson { get; init; } // CLAR (JSON-LD) for the unit
    public required IReadOnlyList<BusinessRule> Rules { get; init; }
    public required SourceLanguage SourceLanguage { get; init; }
    public string TargetStack { get; init; } = "dotnet8"; // e.g. "dotnet8", "typescript5"
    /// <summary>Feature vector used by the router (complexity, domain, context size, etc.).</summary>
    public IReadOnlyDictionary<string, double> FeatureVector { get; init; } =
        new Dictionary<string, double>();
}

/// <summary>One emitted modern source file.</summary>
public sealed record EmittedFile
{
    public required string Path { get; init; }
    public required string Content { get; init; }
    public required SourceLanguage Language { get; init; }
}

/// <summary>The result of a transformation, including which agent produced it and quality signals.</summary>
public sealed record TransformResult
{
    public required string TaskId { get; init; }
    public required IReadOnlyList<EmittedFile> Files { get; init; }
    public required string AgentId { get; init; } // e.g. "claude-code", "offline-replay"
    public required OrchestratorMode Mode { get; init; }
    /// <summary>SHA-256 of the prompt/context used (provenance), if applicable.</summary>
    public string? PromptSha256 { get; init; }
    public bool CompiledClean { get; init; }
    public double QualityEstimate { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

// ─────────────────────────────────────────────────────────────────────────────
// Validation model (behavioral equivalence)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Discrete = exact equality required; Continuous = bounded relative error allowed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OracleKind { Discrete, Continuous }

/// <summary>Tolerance configuration for the equivalence comparison.</summary>
public sealed record ToleranceConfig
{
    /// <summary>Relative error bound for continuous outputs (e.g. 1e-9). Discrete outputs use 0.</summary>
    public double ContinuousRelativeError { get; init; } = 1e-9;
    /// <summary>Absolute floor to avoid divide-by-zero on near-zero continuous values.</summary>
    public double ContinuousAbsoluteFloor { get; init; } = 1e-12;
}

/// <summary>One (input, expected-output) golden vector for differential testing.</summary>
public sealed record EquivalenceTestVector
{
    public required string Id { get; init; }
    public required string InputJson { get; init; }
    public required string ExpectedOutputJson { get; init; }
    /// <summary>Tags such as "anti-meridian", "leap-second", "overflow", "nominal".</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}

/// <summary>Per-named-output (oracle) equivalence outcome.</summary>
public sealed record OracleResult
{
    public required string OracleName { get; init; } // e.g. "GreatCircleDistanceNm", "TaskingGoNoGo"
    public required OracleKind Kind { get; init; }
    public int VectorsEvaluated { get; init; }
    public int Violations { get; init; }
    public double MaxObservedRelativeError { get; init; }
    /// <summary>True when the modern output differs from a KNOWN-WRONG legacy output (a finding, not a failure).</summary>
    public bool IsIntentionalDivergence { get; init; }
}

/// <summary>The behavioral-equivalence report. results/equivalence-report.json.</summary>
public sealed record EquivalenceReport
{
    public required string UnitId { get; init; }
    public required int VectorsTotal { get; init; }
    public required int VectorsPassed { get; init; }
    public required int Violations { get; init; }
    public required IReadOnlyList<OracleResult> Oracles { get; init; }
    /// <summary>
    /// The headline upper confidence bound on the per-vector operational-deviation probability,
    /// for ZERO violations in N trials: the "rule of three" <c>ln(1/(1−ConfidenceLevel))/N</c>
    /// (the 95% bound is ≈ 3/N = ln(20)/N). Reported at <see cref="ConfidenceLevel"/> (95% by
    /// default). Field name retained for contract compatibility; this is NOT the raw ln(1/δ)/N
    /// exponential statistic — that mislabeled value (≈5e-7 at δ=0.999) understated risk and was
    /// removed. Lower is better; shrinks as 1/N. In the zero-violation regime only.
    /// </summary>
    public double ChernoffDeviationBound { get; init; }
    /// <summary>The CONFIDENCE level (1−α) for <see cref="ChernoffDeviationBound"/>; 0.95 (95%) by default.</summary>
    public double ConfidenceLevel { get; init; }
    /// <summary>
    /// A secondary, more-conservative upper confidence bound at <see cref="SecondaryConfidenceLevel"/>
    /// (99.9%): <c>ln(1/(1−0.999))/N = ln(1000)/N</c> (≈3.454e-3 at N=2000). Null when not computed.
    /// </summary>
    public double? SecondaryUpperConfidenceBound { get; init; }
    /// <summary>The confidence level for <see cref="SecondaryUpperConfidenceBound"/> (e.g. 0.999). Null when not computed.</summary>
    public double? SecondaryConfidenceLevel { get; init; }
    /// <summary>System-level bound from the Equivalence-Composability theorem across the unit DAG.</summary>
    public double? ComposedSystemBound { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

// ─────────────────────────────────────────────────────────────────────────────
// Cyber / cATO model
// ─────────────────────────────────────────────────────────────────────────────

public sealed record StigFinding
{
    public required string RuleId { get; init; } // e.g. "APSC-DV-002400" (App Sec & Dev STIG V-ID)
    public required string Title { get; init; }
    public required string Severity { get; init; } // CAT I|II|III
    public required string Location { get; init; }
    public bool RemediatedByTransform { get; init; }

    /// <summary>
    /// Honest remediation disposition of a reconciled "after" finding (additive; null on "before"
    /// findings). One of:
    ///   "Remediated"  — the finding's legacy source is a C# file WITHIN the modern transform's
    ///                   scope AND the vulnerable pattern is absent from the modern C#. Only a
    ///                   genuinely-fixed finding gets this value; <see cref="RemediatedByTransform"/>
    ///                   == (Disposition == "Remediated").
    ///   "OutOfScope"  — the finding's legacy source is a file TYPE the modern component does not
    ///                   cover (e.g. .js UI, .sql DDL). Flagged for a follow-on increment, NOT
    ///                   claimed fixed (absence of those files ≠ a fix).
    ///   "Residual"    — the finding's legacy source IS an in-scope C# file, but the pattern still
    ///                   persists in the modern C# (carried as a residual hardening POA&amp;M item).
    /// </summary>
    public string? Disposition { get; init; }
}

public sealed record ControlMapping
{
    public required string ControlId { get; init; } // e.g. "SI-10", "SC-13"
    public required string ControlName { get; init; }
    public required IReadOnlyList<string> EvidenceRefs { get; init; } // check ids / file paths
}

public sealed record PoamItem
{
    public required string Id { get; init; }
    public required string Weakness { get; init; }
    public required string Status { get; init; } // Open|Remediated|Risk-Accepted
    public string? ScheduledCompletion { get; init; }
}

/// <summary>One tamper-evident provenance record for a pipeline action (Merkle/hashchain leaf).</summary>
public sealed record ProvenanceRecord
{
    public required string Sequence { get; init; }
    public required string Action { get; init; } // "transform", "validate", "stig-scan", ...
    public required string Actor { get; init; } // agent/module id
    public required string PayloadSha256 { get; init; }
    public required string PrevHash { get; init; }
    public required string EntryHash { get; init; }
}

/// <summary>The bundle of continuous-ATO artifacts the overlay emits.</summary>
public sealed record CatoArtifacts
{
    public required IReadOnlyList<StigFinding> StigBefore { get; init; }
    public required IReadOnlyList<StigFinding> StigAfter { get; init; }
    public required IReadOnlyList<ControlMapping> ControlMap { get; init; }
    public required IReadOnlyList<PoamItem> Poam { get; init; }
    /// <summary>Path to the generated CycloneDX SBOM (results/sbom.cdx.json).</summary>
    public required string SbomPath { get; init; }
    public required string ProvenanceMerkleRoot { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Governance model
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A human-in-the-loop review gate decision.</summary>
public sealed record ReviewGate
{
    public required string GateId { get; init; } // "KG1", "KG2", "H0", ...
    public required string Description { get; init; }
    public bool Passed { get; init; }
    public IReadOnlyDictionary<string, string> Evidence { get; init; } =
        new Dictionary<string, string>();
}
