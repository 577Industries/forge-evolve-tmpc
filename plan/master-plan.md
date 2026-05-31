# Master Plan — FORGE EVOLVE for TMPC

This is the execution map for the build. Full program plan (incl. proposal) lives at
`~/.claude/plans/hello-claude-today-i-vast-narwhal.md`.

## Frozen integration contracts (Phase 0 — DO NOT break)
These are the seams every workstream builds against. Additive changes OK; breaking changes require a
re-freeze commit referencing this section.
- **Module interfaces / DTOs:** `src/ForgeEvolve.Contracts/{Interfaces,Models}.cs`
  (`IDiscoveryEngine`, `IClarProvider`, `IMigrationPlanner`, `IToolOrchestrator`, `ITransformer`,
  `IEquivalenceValidator`, `ILegacyRunner`, `IModernRunner`, `ICyberOverlay`, `IGovernance`).
- **CLAR schema:** `clar-spec/CLAR.schema.json` (the source↔target decoupling contract).
- **CETM schema + validator:** `evidence/{cetm.json,validate-cetm.mjs}` (claim→evidence honesty gate).
- **Surrogate answer-key + corpus formats:** defined with the surrogate in Phase 1.

## Pipeline (dependency order)
`Discovery → CLAR → Planner → Orchestrator/Transformer → Validator → CyberOverlay`, Governance records all.

## Workstreams (Phase 2 — parallel, isolated worktrees)
| WS | Project | Depends on | Notes |
|---|---|---|---|
| A | `src/ForgeEvolve.Discovery` | Contracts, surrogate fixtures | Roslyn + Tree-sitter, dep graph, Tarjan SCC, rule extraction, crypto inventory |
| B | `clar-spec` + `src/ForgeEvolve.Clar` | Contracts, CLAR schema | lift C#/JS/VB6/SQL → CLAR; precision-constrained mapping |
| C | `src/ForgeEvolve.Planner` | Contracts | risk score, spectral cluster, boundaries, ordering |
| D | `orchestrator` (TS) + `src/ForgeEvolve.Orchestrator` | Contracts, transcript cache | model-router, offline/local/cloud |
| E | `src/ForgeEvolve.Transformation` | Contracts, CLAR | emit modern .NET 8 |
| F | `src/ForgeEvolve.Validation` | Contracts, surrogate | differential + CsCheck + mission oracles + Chernoff |
| G | `src/ForgeEvolve.Cato` | Contracts, hashchain-audit | STIG, 800-53, CycloneDX SBOM, provenance, POA&M |
| H | `src/ForgeEvolve.Governance` + `src/ForgeEvolve.Cli` | Contracts | audit trail, review gates, the `make demo` driver |

Per-WS exit gate: builds, unit tests green, contract conformance, no secrets/ITAR strings.

## Phases
- **P0 Scaffold + contracts** ✅ (this commit baseline)
- **P1 Surrogate + golden corpus** — `surrogate/`, gate H1
- **P2 Module fan-out** — table above, parallel worktrees
- **P3 Integration + `make demo` + CI** — merge in dependency order; clean-clone + double-run determinism
- **P4 Proposal volumes + CETM** — `../proposal/`
- **P5 Adversarial verification (5 auditors, loop-until-clean)** — gate H5
- **P6 Publish prep + DSIP guide** — gates H6/H7

## Execution log
- 2026-05-31 — P0 started: repo init, .NET SDK 8.0.421 pinned, contracts authored & building, CLAR
  schema + CETM validator in place.
