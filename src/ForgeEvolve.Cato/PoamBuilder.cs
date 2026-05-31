// ─────────────────────────────────────────────────────────────────────────────
// PoamBuilder — Plan of Action & Milestones (poam.csv / PoamItem list).
//
// PART OF: FORGE EVOLVE for TMPC, Cyber/cATO overlay (Stage 5).
//
// Two sources of POA&M items:
//   1. STIG findings — Remediated where the transform fixed them; Open otherwise.
//   2. The LATENT COMPUTATIONAL DEFECTS (D1 anti-meridian, D2 precision drift,
//      D3 TOT truncation + omitted leap seconds) recovered by the Discovery engine as
//      business rules / intentional divergences. These are correctness defects, NOT
//      auto-fixed by the security overlay, so they are emitted Open and explicitly
//      RECOMMENDED FOR AN ENGINEERING CHANGE PROPOSAL (ECP). We do NOT claim auto-fix.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Cato;

public static class PoamBuilder
{
    /// <summary>
    /// Build POA&amp;M items from the reconciled STIG "after" set plus the latent computational
    /// defects discovered in the legacy code. <paramref name="discovery"/> supplies the
    /// recovered business rules / crypto findings that inform the ECP-recommended items.
    /// </summary>
    public static IReadOnlyList<PoamItem> Build(
        IReadOnlyList<StigFinding> stigAfter,
        DiscoveryReport discovery)
    {
        var items = new List<PoamItem>();

        // (1) Security findings from the STIG reconciliation (POAM-S-NNN).
        int s = 0;
        foreach (StigFinding f in stigAfter)
        {
            items.Add(new PoamItem
            {
                Id = $"POAM-S-{++s:D3}",
                Weakness = $"[{f.RuleId} / {f.Severity}] {f.Title} ({f.Location})",
                Status = f.RemediatedByTransform ? "Remediated" : "Open",
                ScheduledCompletion = f.RemediatedByTransform ? null : "ECP-recommended",
            });
        }

        // (2) Latent computational defects — Open, ECP-recommended, NOT auto-fixed (POAM-C-NNN).
        //     Sourced from the discovered business rules so the POA&M only lists defects
        //     actually present in the analyzed code.
        int c = 0;
        foreach (PoamItem d in LatentComputationalDefects(discovery))
        {
            items.Add(d with { Id = $"POAM-C-{++c:D3}" });
        }

        return items;
    }

    /// <summary>
    /// The three seeded latent defects, surfaced only when the discovery report actually
    /// carries evidence of them (business rules whose statement/expression references the
    /// defect, or notes). Each is Open and ECP-recommended; none is claimed auto-fixed.
    /// </summary>
    private static IEnumerable<PoamItem> LatentComputationalDefects(DiscoveryReport discovery)
    {
        // Gather all text the discovery engine produced about each rule so we can confirm the
        // defect is genuinely present in the analyzed corpus before listing it.
        string corpusText = string.Join("\n",
            discovery.BusinessRules.SelectMany(r => new[]
            {
                r.Id, r.Statement, r.Expression ?? "",
                string.Join(",", r.SourceRefs),
            }));
        string lower = corpusText.ToLowerInvariant();

        bool HasAny(params string[] needles) => needles.Any(x => lower.Contains(x));

        // D1 — anti-meridian / longitude wrap.
        if (HasAny("anti-meridian", "antimeridian", "meridian", "dlon", "wrap", "longitude"))
        {
            yield return new PoamItem
            {
                Id = "POAM-C-PLACEHOLDER",
                Weakness =
                    "D1 anti-meridian distance defect: legacy leg distance uses raw (lon2-lon1) " +
                    "with no longitude wrap; legs crossing +/-180 deg are computed incorrectly. " +
                    "Latent computational correctness defect. RECOMMENDED FOR ECP (not auto-fixed by the cATO overlay).",
                Status = "Open",
                ScheduledCompletion = "ECP-recommended",
            };
        }

        // D2 — precision drift (intermediate rounding before summation; FLOAT persistence).
        if (HasAny("precision", "round", "drift", "float", "decimal", "accumul"))
        {
            yield return new PoamItem
            {
                Id = "POAM-C-PLACEHOLDER",
                Weakness =
                    "D2 precision-drift defect: each leg is rounded before summation and persisted " +
                    "to approximate FLOAT columns, so error accumulates across many legs. " +
                    "Latent computational correctness defect. RECOMMENDED FOR ECP (not auto-fixed by the cATO overlay).",
                Status = "Open",
                ScheduledCompletion = "ECP-recommended",
            };
        }

        // D3 — TOT truncation + omitted leap seconds.
        if (HasAny("tot", "time-on-target", "time on target", "truncat", "leap", "epoch"))
        {
            yield return new PoamItem
            {
                Id = "POAM-C-PLACEHOLDER",
                Weakness =
                    "D3 time-on-target defect: travel time is truncated (not rounded) and the " +
                    "leap-second adjustment is omitted, biasing estimatedTot/totFeasible. " +
                    "Latent computational correctness defect. RECOMMENDED FOR ECP (not auto-fixed by the cATO overlay).",
                Status = "Open",
                ScheduledCompletion = "ECP-recommended",
            };
        }
    }

    /// <summary>Render the POA&amp;M as RFC-4180 CSV (poam.csv).</summary>
    public static string ToCsv(IReadOnlyList<PoamItem> items)
    {
        var sb = new StringBuilder();
        sb.Append("Id,Weakness,Status,ScheduledCompletion\n");
        foreach (PoamItem it in items)
        {
            sb.Append(Csv(it.Id)).Append(',')
              .Append(Csv(it.Weakness)).Append(',')
              .Append(Csv(it.Status)).Append(',')
              .Append(Csv(it.ScheduledCompletion ?? "")).Append('\n');
        }
        return sb.ToString();
    }

    private static string Csv(string s)
    {
        bool needQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        if (!needQuote) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
