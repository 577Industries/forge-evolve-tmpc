# Companion - Proposal ↔ Code Index

This document maps each Phase-I objective and key proposal claim to the exact file, test, or artifact
in this repository that backs it. It is the reviewer's "canonical index" (the HELIOS pattern). Rows are
populated as modules land; the Claim→Evidence Traceability Matrix (`evidence/cetm.json`) is the
machine-validated companion to this human-readable map.

> Reminder: all results are measured on the synthetic, unclassified surrogate and are preliminary.

## Phase I objective → evidence
| Topic Phase-I ask | Where in the code | Where in the proposal |
|---|---|---|
| 1. Analyze TMPC software architecture; identify modernization needs | `src/ForgeEvolve.Discovery/*`; `results/run/discovery-report.json` | Vol 2 §(b) O1, §(c) T-B |
| 2. Conceptual AI models for analysis, refactoring, cybersecurity | `clar-spec/*`, `src/ForgeEvolve.{Clar,Planner,Orchestrator,Transformation}/*` | Vol 2 §(b) O2, §(c) T-C/T-D |
| 3. PoC AI-driven refactoring on a representative component | `src/ForgeEvolve.{Transformation,Validation}/*`; `tmpc-modern-mds/`; `results/run/equivalence-report.json` | Vol 2 §(b) O3, §(c) T-D/T-E |
| 4. Phase II prototype plans + cyber integration | `src/ForgeEvolve.Cato/*`; `results/run/{stig-findings.*,control-map.yaml,sbom.cdx.json,poam.csv,provenance.json}`; `results/run/migration-plan.json` | Vol 2 §(b) O4, §(c) T-F, §(e) |

## Key claim → artifact (proven; surrogate-based, preliminary)
A committed reference run lives in `results/reference/`; reviewers can reproduce it with `make demo`.

| Claim | CETM id | Artifact / test | Verify |
|---|---|---|---|
| No real/controlled TMPC data is present | C-DATA-EXCL | `EXCLUSIONS.md` | `cat EXCLUSIONS.md` |
| Modern component is behaviorally equivalent to legacy: **2000/2000 vectors, 0 violations** | C-EQUIV-PASS | `results/reference/equivalence-report.json`; `tmpc-modern-mds/tools/ModernCheck` | `make demo` → MODERN-CHECK PASS 2000/2000 |
| Equivalence confidence: **95% upper confidence bound 1.498e-3** (rule of three, ln(20)/N) for 0 violations in N=2000; secondary 99.9% bound 3.454e-3 (ln(1000)/N) | C-EQUIV-CHERNOFF | `results/reference/equivalence-report.json` (`chernoffDeviationBound` @ `confidenceLevel`=0.95; `secondaryUpperConfidenceBound` @ 0.999) | `make demo` |
| Refactor cuts god-method cyclomatic complexity **49 → 6** | C-CC-REDUCE | Discovery vs modern; `results/reference/transform-result.json` Notes | `make demo` |
| Discovery parses C# at **100%**, extracts rules at **F1=1.0** vs gold | C-DISC-F1 | `ForgeEvolve.Discovery.Tests`; `results/reference/discovery-report.json` | `dotnet test` |
| cATO: detects **5 STIG findings**; genuinely remediates the **CAT I SQL-injection**; JS-XSS + SQL-DDL out-of-scope; TLS + hardcoded-cred residual; 10×800-53 + SBOM + POA&M | C-CATO-STIG | `results/reference/cato/*`, `sbom.cdx.json`, `poam.csv` | `make demo` |
| Tamper-evident provenance (SHA-256 IGOM + Merkle root); KG1/KG2 pass | C-GOV-IGOM | `results/reference/provenance.json`; `ForgeEvolve.Governance.Tests` | `dotnet test` |
| Demo is byte-deterministic (offline, keyless) | C-REPRO-DET | `scripts/verify-reproducible.sh` | `make verify` |
| Validation engine detects latent legacy defects at **P=R=1.0** on 321 vectors | C-DIV-DETECT | `ForgeEvolve.Validation.Tests` (divergence detector) | `dotnet test` |
| Live model routing reuses the published `@577-industries/model-router` | C-REUSE-ROUTER | `orchestrator/` (npm dep `^1.0.0`) | `cd orchestrator && npm test` |
