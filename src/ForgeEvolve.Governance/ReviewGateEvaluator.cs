// FORGE EVOLVE for TMPC — Governance workstream (WS-H).
//
// ReviewGateEvaluator encodes the human-in-the-loop review gates named in
// governance/REVIEW_GATES.md and governance/pre-registration.md, and turns supplied evidence
// into a pass/fail decision (a Contracts.ReviewGate).
//
// Thresholds are the PRE-REGISTERED ones (frozen before runs), so a reviewer can diff the gate
// logic here against pre-registration.md:
//   * KG1 (KG#1, end Month 3): rule-extraction F1 >= 0.85 AND oracle harness runs end-to-end.
//   * KG2 (KG#2, end Month 6): 0 discrete violations AND the cATO artifact bundle was generated.
//   * Per-component pipeline gates (REVIEW_GATES.md): Design, Translation, Acceptance — each
//     requires the human sign-off plus the specific evidence that gate is meant to carry.
//
// Each evaluation is itself recorded to the ledger (provenance of the decision): the gate id,
// outcome, and a canonical JSON of the evidence become a tamper-evident entry. That is the
// "decisions are recorded" requirement from REVIEW_GATES.md, made cryptographic.

using System.Globalization;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Governance;

/// <summary>
/// Evaluates a named review gate against supplied evidence and records the decision to the
/// governance hashchain. Gate ids and thresholds are frozen per pre-registration.md.
/// </summary>
public sealed class ReviewGateEvaluator
{
    private readonly HashchainLedger _ledger;

    public ReviewGateEvaluator(HashchainLedger ledger)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    /// <summary>Action label used for the provenance entry of a gate decision.</summary>
    public const string GateDecisionAction = "review-gate-decision";

    /// <summary>The gate ids this evaluator knows how to score, in a stable order.</summary>
    public static readonly IReadOnlyList<string> KnownGateIds = new[]
    {
        "KG1", "KG2",
        "Design", "Translation", "Acceptance",
    };

    /// <summary>
    /// Evaluate <paramref name="gateId"/> against <paramref name="evidence"/>, append a provenance
    /// record for the decision, and return the resulting <see cref="ReviewGate"/>.
    /// Unknown gate ids fail closed (Passed = false) with an explanatory description.
    /// </summary>
    public ReviewGate Evaluate(string gateId, IReadOnlyDictionary<string, string> evidence)
    {
        ArgumentNullException.ThrowIfNull(gateId);
        evidence ??= new Dictionary<string, string>();

        var (passed, description) = Score(gateId, evidence);

        // Copy evidence into an immutable snapshot for the gate record.
        var snapshot = new Dictionary<string, string>(evidence, StringComparer.Ordinal);

        // Record the decision (provenance of the gate). The payload is canonical so the entry
        // hash is deterministic for a given (gateId, outcome, evidence) tuple.
        _ledger.Append(
            GateDecisionAction,
            actor: $"governance:gate:{gateId}",
            payloadJson: CanonicalDecisionPayload(gateId, passed, snapshot));

        return new ReviewGate
        {
            GateId = gateId,
            Description = description,
            Passed = passed,
            Evidence = snapshot,
        };
    }

    // ── gate logic ──────────────────────────────────────────────────────────────────────────

    private static (bool Passed, string Description) Score(
        string gateId, IReadOnlyDictionary<string, string> e)
    {
        switch (gateId)
        {
            // KG#1 — end of Month 3 (Base): F1 >= 0.85 AND oracle harness runs end-to-end.
            case "KG1":
            {
                var f1Ok = TryGetDouble(e, "ruleF1", out var f1) && f1 >= 0.85;
                var harnessOk = IsTrue(e, "oracleHarnessRuns");
                var passed = f1Ok && harnessOk;
                return (passed,
                    "KG#1 (end Month 3): rule-extraction F1 >= 0.85 AND mission-data-aware oracle " +
                    "harness runs end-to-end on the surrogate. PASS -> continue full scope.");
            }

            // KG#2 — end of Month 6 (Base): 0 discrete violations AND cATO bundle generated.
            case "KG2":
            {
                var zeroViolations = TryGetInt(e, "discreteViolations", out var v) && v == 0;
                var catoOk = IsTrue(e, "catoBundle");
                var passed = zeroViolations && catoOk;
                return (passed,
                    "KG#2 (end Month 6): 0 discrete equivalence violations AND the cATO artifact " +
                    "bundle auto-generated. PASS -> recommend Option/Phase II.");
            }

            // Per-component Design gate (after Discovery + Planning): human approves the proposed
            // microservice boundary and migration-unit scope.
            case "Design":
            {
                var approved = IsTrue(e, "humanApproved");
                var hasBoundary = HasNonEmpty(e, "boundaryApproved") && IsTrue(e, "boundaryApproved");
                var hasScope = HasNonEmpty(e, "unitScope");
                var passed = approved && hasBoundary && hasScope;
                return (passed,
                    "Design gate (post Discovery+Planning): human approves the proposed microservice " +
                    "boundary and migration-unit scope.");
            }

            // Per-component Translation gate (after Transformation): human reviews emitted modern
            // code, the rules it must honor, and the diff.
            case "Translation":
            {
                var approved = IsTrue(e, "humanApproved");
                var diffReviewed = IsTrue(e, "diffReviewed");
                var rulesHonored = IsTrue(e, "rulesHonored");
                var compiledClean = IsTrue(e, "compiledClean");
                var passed = approved && diffReviewed && rulesHonored && compiledClean;
                return (passed,
                    "Translation gate (post Transformation): human reviews emitted modern code, the " +
                    "extracted business rules it must honor, and the diff.");
            }

            // Per-component Acceptance gate (after Validation): human reviews the equivalence report
            // (0 discrete violations, continuous within tolerance) and the cATO deltas.
            case "Acceptance":
            {
                var approved = IsTrue(e, "humanApproved");
                var zeroDiscrete = TryGetInt(e, "discreteViolations", out var dv) && dv == 0;
                var continuousOk = IsTrue(e, "continuousWithinTolerance");
                var catoReviewed = IsTrue(e, "catoDeltaReviewed");
                var passed = approved && zeroDiscrete && continuousOk && catoReviewed;
                return (passed,
                    "Acceptance gate (post Validation): human reviews the equivalence report " +
                    "(discrete violations, per-oracle deltas, intentional divergences) and the " +
                    "cATO deltas before the component is accepted into the modern baseline.");
            }

            default:
                return (false,
                    $"Unknown gate id '{gateId}'. Known gates: {string.Join(", ", KnownGateIds)}. " +
                    "Fails closed.");
        }
    }

    // ── evidence helpers (culture-invariant, fail-closed on missing/malformed) ────────────────

    private static bool HasNonEmpty(IReadOnlyDictionary<string, string> e, string key) =>
        e.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v);

    private static bool IsTrue(IReadOnlyDictionary<string, string> e, string key) =>
        e.TryGetValue(key, out var v) &&
        string.Equals(v?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetDouble(IReadOnlyDictionary<string, string> e, string key, out double value)
    {
        value = 0;
        return e.TryGetValue(key, out var v) &&
               double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string> e, string key, out int value)
    {
        value = 0;
        return e.TryGetValue(key, out var v) &&
               int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Canonical, deterministic JSON for a gate decision: gate id, outcome, and the evidence
    /// keys sorted ordinally. Stable ordering is what makes the recorded entry's hash reproducible.
    /// </summary>
    internal static string CanonicalDecisionPayload(
        string gateId, bool passed, IReadOnlyDictionary<string, string> evidence)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        sb.Append("\"gateId\":").Append(JsonString(gateId)).Append(',');
        sb.Append("\"passed\":").Append(passed ? "true" : "false").Append(',');
        sb.Append("\"evidence\":{");
        var first = true;
        foreach (var kv in evidence.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(JsonString(kv.Key)).Append(':').Append(JsonString(kv.Value));
        }
        sb.Append("}}");
        return sb.ToString();
    }

    private static string JsonString(string? s)
    {
        if (s is null) return "null";
        var sb = new System.Text.StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
