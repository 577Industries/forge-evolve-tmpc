# Artifacts & Claims

!!! warning "Surrogate-based · preliminary · not government-validated"
    Every claim below is measured on the **synthetic, unclassified** surrogate in the repository and is
    **preliminary** — not validated against government TMPC code, not validated by the Navy, and implying
    no Navy endorsement. Reviewers reproduce the A-rows with `make demo` / `dotnet test`. See
    **[Honesty & Exclusions](honesty.md)** and **[Reproducibility](reproducibility.md)**.

This page is the claim&rarr;artifact registry. Each artifact link is a **deep link pinned to the release
tag** `v0.1.0-sbir-DON26BZ01-NV013`, so the URL survives the final commit re-freeze. The
machine-validated companion is `evidence/cetm.json`; the human-readable map is the
**[Proposal Companion](companion.md)**.

A committed reference run lives in `results/reference/`; reviewers reproduce it with `make demo`.

| Claim | CETM id | Artifact / test | Verify |
|---|---|---|---|
| No real/controlled TMPC data is present | `C-DATA-EXCL` | [`EXCLUSIONS.md`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/EXCLUSIONS.md) | `cat EXCLUSIONS.md` |
| Modern component is behaviorally equivalent to legacy: **2000/2000 vectors, 0 violations** | `C-EQUIV-PASS` | [`results/reference/equivalence-report.json`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/results/reference/equivalence-report.json); [`tmpc-modern-mds/tools/ModernCheck`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/tmpc-modern-mds) | `make demo` → MODERN-CHECK PASS 2000/2000 |
| Equivalence confidence: **95% upper confidence bound 1.498e-3** (rule of three, ln(20)/N) for 0 violations in N=2000; secondary 99.9% bound 3.454e-3 (ln(1000)/N) | `C-EQUIV-CHERNOFF` | [`results/reference/equivalence-report.json`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/results/reference/equivalence-report.json) | `make demo` |
| Refactor cuts god-method cyclomatic complexity **49 → 6** | `C-CC-REDUCE` | [`results/reference/transform-result.json`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/results/reference/transform-result.json) Notes | `make demo` |
| Discovery parses C# at **100%**, extracts rules at **F1=1.0** vs gold | `C-DISC-F1` | [`ForgeEvolve.Discovery.Tests`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/src); [`results/reference/discovery-report.json`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/results/reference/discovery-report.json) | `dotnet test` |
| cATO: detects **5 STIG findings**; genuinely remediates the **CAT I SQL-injection**; JS-XSS + SQL-DDL out-of-scope; TLS + hardcoded-cred residual; 10×800-53 + SBOM + POA&M | `C-CATO-STIG` | [`results/reference/cato/`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/results/reference), [`sbom.cdx.json`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/results/reference), [`poam.csv`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/results/reference) | `make demo` |
| Tamper-evident provenance (SHA-256 IGOM + Merkle root); KG1/KG2 pass | `C-GOV-IGOM` | [`results/reference/provenance.json`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/results/reference/provenance.json); [`ForgeEvolve.Governance.Tests`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/src) | `dotnet test` |
| Demo is byte-deterministic (offline, keyless) | `C-REPRO-DET` | [`scripts/verify-reproducible.sh`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/scripts/verify-reproducible.sh) | `make verify` |
| Validation engine detects latent legacy defects at **P=R=1.0** on 321 vectors | `C-DIV-DETECT` | [`ForgeEvolve.Validation.Tests`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/src) (divergence detector) | `dotnet test` |
| Live model routing reuses the published `@577-industries/model-router` | `C-REUSE-ROUTER` | [`orchestrator/`](https://github.com/577Industries/forge-evolve-tmpc/blob/v0.1.0-sbir-DON26BZ01-NV013/orchestrator) (npm dep `^1.0.0`) | `cd orchestrator && npm test` |
