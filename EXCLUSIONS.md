# Export-Control & Data-Provenance Statement (EXCLUSIONS)

**This repository is UNCLASSIFIED and contains NO export-controlled technical data.**

The U.S. Navy SBIR topic DON26BZ01-NV013 is restricted under ITAR (22 CFR 120-130).
To remain unambiguously publishable and reviewer-runnable, this repository was built
to contain **none** of the following:

| Excluded | Status in this repo |
|---|---|
| Real Theater Mission Planning Center (TMPC) source code | **Not present.** The `surrogate/` component is synthetic, authored from scratch. |
| Real Mission Distribution System (MDS) / TED / TMT code | **Not present.** |
| Real tasking, targeting, or mission data | **Not present.** All inputs in `surrogate/corpus/` are randomly generated from a fixed seed. |
| Real geographic coordinates of operational interest | **Not present.** Coordinates are synthetic test fixtures. |
| Real Tomahawk Weapon System algorithms or parameters | **Not present.** The surrogate models *shapes* of mission-planning logic (route validation, anti-meridian handling, time-on-target), not any real algorithm. |
| Government-furnished information (GFI), DRs, or CRs | **Not present.** None has been received; Phase I access is "discussed with awardees." |
| Secrets, API keys, credentials | **Not present.** CI runs a secret scan; `make demo` runs offline with no keys. |

## What the surrogate *is*
`surrogate/tmpc-surrogate-mds` is a **synthetic, intentionally legacy** C#/.NET (+ VB6 + JavaScript
+ SQL) application engineered to exhibit the **same classes of technical debt and the same shapes of
mission-planning logic** that the government described in the topic Q&A (a ~1.3M-LOC, mostly-C# MDS
with SQL and JavaScript). It is a stand-in that lets reviewers run the modernization pipeline
end-to-end without any controlled data. `surrogate/DEBT.md` maps each synthetic debt item to its
*plausible* real-MDS analog without claiming fidelity to any real system.

## Handling of controlled data in contract performance
Any Phase I work that touches the government-furnished MDS VM build, DRs/CRs, or other controlled
TMPC technical data is performed **US-persons-only**, inside a CUI enclave (DFARS 252.204-7012), and
is **NOT** published here. Only the synthetic surrogate, the framework, and the harness are public.

Questions: t.waweru@577industries.com
