// Tasking responsibility, extracted from the legacy god method.
//
// BEHAVIOR-PRESERVING and BUG-FREE: the purely categorical GO/NO-GO rule, preserved exactly.
//   GO requires routeValid AND NOT (variant == "MST" AND platform == "SSN").
//   MST is surface-only, so MST-on-SSN is always NO-GO.

namespace ForgeEvolve.ModernMds.Tasking;

/// <summary>Evaluates the categorical tasking GO/NO-GO decision. Single responsibility.</summary>
public interface ITaskingEvaluator
{
    bool Evaluate(bool routeValid, string platform, string variant);
}

/// <inheritdoc />
public sealed class TaskingEvaluator : ITaskingEvaluator
{
    public bool Evaluate(bool routeValid, string platform, string variant)
    {
        if (!routeValid)
        {
            return false;
        }
        if (variant == "MST" && platform == "SSN")
        {
            return false;
        }
        return true;
    }
}
