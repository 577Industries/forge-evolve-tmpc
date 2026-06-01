// MissionService — the modern orchestration root that REPLACES the legacy god method.
//
// The legacy MissionProcessor.ProcessMission was one ~200-line method (cyclomatic complexity 49)
// that inlined parse + pre-validate diagnostics + route-validation + distance + TOT + tasking +
// publish + serialize. Here, each of those is a single-responsibility, injected collaborator
// (IMissionParser, IRouteValidator, IDistanceCalculator, ITotEstimator, ITaskingEvaluator,
// IMissionPublisher, IMissionResultSerializer). MissionService only SEQUENCES them and assembles
// the message list in the exact legacy order.
//
// BEHAVIOR-PRESERVING: the emitted JSON EQUALS the legacy `legacyOutput` for all 2000 corpus
// vectors. The D1/D2/D3 quirks live inside the distance/TOT collaborators and are reproduced
// exactly; they are NOT fixed here (ECP-recommended findings — see README.md).
//
// Message ordering (must match legacy / reference legacy_model):
//   ["LEGACY"] + <validation messages: LEG_OUT_OF_BOX:* then TURN_EXCEEDED:*>
//             + (routeValid ? [] : ["ROUTE_INVALID"])
//             + (taskingGoNoGo ? [] : ["TASKING_NO_GO"])
//             + <optional publish status, only when PublishEnabled=true>

using ForgeEvolve.ModernMds.Distribution;
using ForgeEvolve.ModernMds.Geometry;
using ForgeEvolve.ModernMds.Models;
using ForgeEvolve.ModernMds.Parsing;
using ForgeEvolve.ModernMds.Routing;
using ForgeEvolve.ModernMds.Serialization;
using ForgeEvolve.ModernMds.Tasking;
using ForgeEvolve.ModernMds.Timing;

namespace ForgeEvolve.ModernMds.Services;

/// <summary>
/// The modernized mission component's single entry point. Behaviorally equivalent to the legacy
/// <c>MissionProcessor.ProcessMission</c>.
/// </summary>
public sealed class MissionService
{
    private readonly IMissionParser _parser;
    private readonly IRouteValidator _routeValidator;
    private readonly IDistanceCalculator _distanceCalculator;
    private readonly ITotEstimator _totEstimator;
    private readonly ITaskingEvaluator _taskingEvaluator;
    private readonly IMissionPublisher _publisher;
    private readonly IMissionResultSerializer _serializer;
    private readonly MissionOptions _options;

    public MissionService(
        IMissionParser parser,
        IRouteValidator routeValidator,
        IDistanceCalculator distanceCalculator,
        ITotEstimator totEstimator,
        ITaskingEvaluator taskingEvaluator,
        IMissionPublisher publisher,
        IMissionResultSerializer serializer,
        MissionOptions options)
    {
        _parser = parser;
        _routeValidator = routeValidator;
        _distanceCalculator = distanceCalculator;
        _totEstimator = totEstimator;
        _taskingEvaluator = taskingEvaluator;
        _publisher = publisher;
        _serializer = serializer;
        _options = options;
    }

    /// <summary>
    /// Default-wired instance: behavior-preserving, publish disabled. Equivalent to constructing
    /// the legacy static processor. Provided for callers that don't run a DI container.
    /// </summary>
    public static MissionService CreateDefault(MissionOptions? options = null) =>
        new(
            new MissionParser(),
            new RouteValidator(),
            new DistanceCalculator(),
            new TotEstimator(),
            new TaskingEvaluator(),
            new MissionPublisher(),
            new MissionResultSerializer(),
            options ?? new MissionOptions());

    /// <summary>
    /// Process a MissionRequest JSON string and return a MissionResult JSON string. Synchronous,
    /// legacy-equivalent entry point (the publish path is output-neutral and disabled by default).
    /// </summary>
    public string ProcessMission(string inputJson) =>
        ProcessMissionAsync(inputJson).GetAwaiter().GetResult();

    /// <summary>Async pipeline (the distribution step is async). Behaviorally equivalent to legacy.</summary>
    public async Task<string> ProcessMissionAsync(string inputJson, CancellationToken cancellationToken = default)
    {
        ParseOutcome parsed = _parser.Parse(inputJson);
        if (parsed is ParseOutcome.Error err)
        {
            return _serializer.Serialize(BuildParseErrorResult(err.MissionId, err.LaunchEpochSec));
        }

        var request = ((ParseOutcome.Ok)parsed).Request;
        MissionResult result = await ComputeAsync(request, cancellationToken).ConfigureAwait(false);
        return _serializer.Serialize(result);
    }

    private async Task<MissionResult> ComputeAsync(MissionRequest request, CancellationToken ct)
    {
        RouteValidation validation = _routeValidator.Validate(request.Waypoints, _options);
        DistanceResult distance = _distanceCalculator.Compute(request.Waypoints, _options);
        TotResult tot = _totEstimator.Estimate(
            distance.TotalDistanceNm, request.LaunchEpochSec, request.DesiredTotEpochSec, _options);
        bool taskingGo = _taskingEvaluator.Evaluate(validation.RouteValid, request.Platform, request.Variant);

        string? publishStatus = await _publisher
            .PublishAsync(request, distance, validation.RouteValid, taskingGo, _options, ct)
            .ConfigureAwait(false);

        IReadOnlyList<string> messages = BuildMessages(validation, taskingGo, publishStatus);

        return new MissionResult
        {
            MissionId = request.MissionId,
            LegDistancesNm = distance.LegDistancesNm,
            TotalDistanceNm = distance.TotalDistanceNm,
            RouteValid = validation.RouteValid,
            EstimatedTotEpochSec = tot.EstimatedTotEpochSec,
            TotFeasible = tot.TotFeasible,
            TaskingGoNoGo = taskingGo,
            Messages = messages,
        };
    }

    /// <summary>Assemble the message list in the exact legacy order.</summary>
    private static IReadOnlyList<string> BuildMessages(
        RouteValidation validation, bool taskingGo, string? publishStatus)
    {
        var messages = new List<string> { "LEGACY" };
        messages.AddRange(validation.Messages);
        if (!validation.RouteValid) messages.Add("ROUTE_INVALID");
        if (!taskingGo) messages.Add("TASKING_NO_GO");
        if (publishStatus is not null) messages.Add(publishStatus);
        return messages;
    }

    /// <summary>
    /// Legacy parse-error result: ["LEGACY", "PARSE_ERROR"], empty legs, zeroed distance/flags, and
    /// estimatedTot = the partially-parsed launchEpochSec (faithful to the legacy local-variable
    /// return). No corpus vector hits this path, but it is preserved for byte-fidelity.
    /// </summary>
    private static MissionResult BuildParseErrorResult(string missionId, long launchEpochSec) => new()
    {
        MissionId = missionId,
        LegDistancesNm = Array.Empty<double>(),
        TotalDistanceNm = 0.0,
        RouteValid = false,
        EstimatedTotEpochSec = launchEpochSec,
        TotFeasible = false,
        TaskingGoNoGo = false,
        Messages = new[] { "LEGACY", "PARSE_ERROR" },
    };
}
