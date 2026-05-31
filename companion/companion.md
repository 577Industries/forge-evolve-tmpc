# Companion — Proposal ↔ Code Index

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

## Key claim → artifact (filled during Phase 4)
| Claim | CETM id | Artifact / test | Verify |
|---|---|---|---|
| No real/controlled TMPC data is present | C-EXAMPLE-A | `EXCLUSIONS.md` | `cat EXCLUSIONS.md` |
| _(populated as the proposal is written)_ | | | |
