// Composition root / lightweight dependency-injection wiring for the modern mission component.
//
// The legacy read global static config deep inside one method. The modern component instead
// composes single-responsibility collaborators here and injects immutable MissionOptions. This
// self-contained factory keeps the offline/air-gapped build dependency-free (no external DI
// package needed), while still expressing explicit constructor injection — every collaborator is
// an interface, swappable for tests or alternative implementations.

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
/// Builds a fully-wired <see cref="MissionService"/> from injectable collaborators. Callers may
/// supply their own implementations (e.g. a fake publisher in tests) or accept the defaults.
/// </summary>
public sealed class MissionServiceFactory
{
    private readonly MissionOptions _options;
    private readonly IMissionParser _parser;
    private readonly IRouteValidator _routeValidator;
    private readonly IDistanceCalculator _distanceCalculator;
    private readonly ITotEstimator _totEstimator;
    private readonly ITaskingEvaluator _taskingEvaluator;
    private readonly IMissionPublisher _publisher;
    private readonly IMissionResultSerializer _serializer;

    public MissionServiceFactory(
        MissionOptions? options = null,
        IMissionParser? parser = null,
        IRouteValidator? routeValidator = null,
        IDistanceCalculator? distanceCalculator = null,
        ITotEstimator? totEstimator = null,
        ITaskingEvaluator? taskingEvaluator = null,
        IMissionPublisher? publisher = null,
        IMissionResultSerializer? serializer = null)
    {
        // Defaulting is delegated to Or(...) so this constructor stays trivial (CC 1) — each null
        // check lives in its own tiny method instead of inflating one composition method.
        _options = options ?? new MissionOptions();
        _parser = Or<IMissionParser>(parser, static () => new MissionParser());
        _routeValidator = Or<IRouteValidator>(routeValidator, static () => new RouteValidator());
        _distanceCalculator = Or<IDistanceCalculator>(distanceCalculator, static () => new DistanceCalculator());
        _totEstimator = Or<ITotEstimator>(totEstimator, static () => new TotEstimator());
        _taskingEvaluator = Or<ITaskingEvaluator>(taskingEvaluator, static () => new TaskingEvaluator());
        _publisher = Or<IMissionPublisher>(publisher, static () => new MissionPublisher());
        _serializer = Or<IMissionResultSerializer>(serializer, static () => new MissionResultSerializer());
    }

    /// <summary>Return <paramref name="provided"/> if non-null, else build the default. CC 2.</summary>
    private static T Or<T>(T? provided, Func<T> makeDefault) where T : class =>
        provided ?? makeDefault();

    /// <summary>Construct the wired service.</summary>
    public MissionService Build() => new(
        _parser, _routeValidator, _distanceCalculator, _totEstimator,
        _taskingEvaluator, _publisher, _serializer, _options);
}
