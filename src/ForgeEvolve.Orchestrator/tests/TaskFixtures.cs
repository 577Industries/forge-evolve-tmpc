using ForgeEvolve.Contracts;

namespace ForgeEvolve.Orchestrator.Tests;

/// <summary>Builders for the TransformTask shapes used across the orchestrator tests.</summary>
internal static class TaskFixtures
{
    /// <summary>
    /// The dummy task whose deterministic key matches the committed sample transcript
    /// (Unit.Id="sample-dummy-unit", CSharp, dotnet8).
    /// </summary>
    public static TransformTask SampleDummyTask(IReadOnlyDictionary<string, double>? features = null) => new()
    {
        TaskId = "sample-dummy-task",
        Unit = new MigrationUnit
        {
            Id = "sample-dummy-unit",
            ProposedServiceName = "SampleService",
            MemberModuleIds = new[] { "SampleUnit.Sample.Add" },
        },
        ClarDocumentJson = "{\"@context\":\"https://577industries.com/clar/v1\",\"id\":\"sample\"}",
        Rules = new[]
        {
            new BusinessRule
            {
                Id = "BR-001",
                Category = BusinessRuleCategory.Calculation,
                Statement = "Sum must use checked decimal arithmetic.",
                SourceRefs = new[] { "SampleUnit.Sample.Add" },
                Confidence = 0.99,
            },
        },
        SourceLanguage = SourceLanguage.CSharp,
        TargetStack = "dotnet8",
        FeatureVector = features ?? new Dictionary<string, double>(),
    };

    /// <summary>A task with no recorded transcript (different unit id → different key).</summary>
    public static TransformTask UnknownTask() => SampleDummyTask() with
    {
        TaskId = "unknown-task",
        Unit = new MigrationUnit
        {
            Id = "no-such-unit-zzz",
            ProposedServiceName = "Nope",
            MemberModuleIds = Array.Empty<string>(),
        },
    };
}
