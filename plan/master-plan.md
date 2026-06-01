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
- 2026-05-31 — P0 complete (commit ca413ed): repo init, .NET SDK 8.0.421 pinned, contracts authored &
  building, CLAR schema + CETM validator in place. `make build/audit` green.
- 2026-05-31 — P1 complete (commit a4d103e): synthetic MDS-like surrogate + frozen golden corpus
  (N=2000, seed 577077, sha256 480167…, 16.05% divergent, categoricals preserved 100%, max equiv
  rel-err 9.45e-10). LegacyCheck self-test 2000/2000. Distance kernel = equirectangular (haversine is
  invariant to the anti-meridian wrap defect). Surrogate projects intentionally out of ForgeEvolve.sln.
- 2026-05-31 — P2 Wave 1 complete (merged to main): WS-A Discovery (CC=49, parse 100%, 12 rules F1=1.0,
  crypto inv; 16 tests), WS-B CLAR (4-layer lift validates, precision-constrained coord/TOT; 19 tests),
  WS-G Cyber/cATO (5 real STIG findings→10 controls, CycloneDX SBOM, Merkle provenance, POA&M; 10 tests),
  WS-H Governance (SHA-256 IGOM, KG gates, tamper-detection; 30 tests). All build on main.
  P3 reconcile notes: unify provenance ledger (Governance owns IGOM; Cato consumes) — Cato uses a
  simpler hash formula than Governance; wire all modules into ForgeEvolve.sln at integration.
- 2026-05-31 — P2 Wave 2 complete (merged to main @ 0afa6be): WS-C Planner (god-cluster→service
  boundaries, risk-scored topo order; 17 tests), WS-D Orchestrator (offline transcript replay +
  Thompson routing; real @577-industries/model-router@1.0.0 for live modes; 19+7 tests), WS-E
  Transformation (modern .NET 8, god method CC 49→6, **MODERN-CHECK 2000/2000** behavioral
  equivalence; 6 tests), WS-F Validation (mission-data-aware oracles, **Chernoff 5.0e-7** N=2000,
  intentional-divergence detector **P=R=1.0** on 321 vectors; 15 tests). (WS-E/F failed once on a
  transient socket error mid-read; relaunched fresh — worktrees were clean.)
  ALL 8 modules built, tested, merged. Worktrees/branches cleaned up.

## P3 integration to-dos (reconcile)
1. Add all 9 src projects + tmpc-modern-mds + surrogate + tools to ForgeEvolve.sln (so `make build/test` cover them).
2. Build `src/ForgeEvolve.Cli` driving the full pipeline: Discovery→CLAR→Planner→Orchestrator(replay)→
   Transformation→Validation(legacy vs REAL modern on corpus)→Cato→Governance → writes `results/run/`.
3. Provenance: Governance owns the IGOM ledger; refactor Cato to record via IGovernance (or unify the
   hash formula) so there is ONE provenance chain + Merkle root.
4. Transcript path: Orchestrator reads `src/ForgeEvolve.Orchestrator/fixtures/transcripts/`; the real
   transcript is at top-level `fixtures/transcripts/mission-modernization.json` — point the orchestrator
   at the top-level dir (or copy it in) so offline replay finds it.
5. Wire Validation's ModernRunner Func to tmpc-modern-mds `MissionService.ProcessMission` → emit the REAL
   headline equivalence number (expect 0 violations, 2000 passed, Chernoff 5.0e-7).
6. Wire `scripts/run-demo.sh`/`make demo` to the CLI; `make verify` double-run determinism; `make sbom`.
7. Tidy stray committed demo artifacts (`results/clar/`, `results/equivalence-report.json`) → move under
   `results/reference/` (the committed reference run) or regenerate; keep `results/run/` gitignored.
8. CETM: add real status-A claims for the proven metrics (MODERN-CHECK, Chernoff, CC reduction, STIG, F1).

- 2026-05-31 — P3 complete (commits af8eca9, 4924adb, 17d4ff6): all 23 projects in ForgeEvolve.sln
  (builds clean, 132 tests pass). ForgeEvolve.Cli drives the full pipeline offline/keyless. LIVE headline:
  **2000/2000 equivalent, Violations=0, Chernoff 5.003e-7**; god CC 49→6; STIG 5→2 residual; KG1/KG2 PASS;
  8-record IGOM. `make demo` byte-deterministic (`make verify` PASS). Transcript path + provenance unified.
  Committed reference run in results/reference/. Companion + CETM populated (10 A / 2 E / 2 P, issues_count=0).
  CI (demo-offline.yml) runs build+test+demo+audit+secret-scan on push.
  Deferred polish (P5): surface the latent-defect detection (321 vs reference) in the demo output, not only tests.
- 2026-05-31 — P4 started: weasyprint render pipeline (proposal/render.py); Vol2_Technical.md drafted &
  renders to 7pp @10pt (≤10). Honesty fix applied: STIG framing corrected — only the CAT I SQL-injection
  is GENUINELY remediated; JS-XSS + SQL-DDL are out-of-transform-scope (not "fixed"); TLS + hardcoded-cred
  are residual POA&M. Updated companion + CETM to match.
  **P5 MUST-FIX (honesty):** the Cato module marks JS-XSS/SQL-DDL as remediatedByTransform=true purely
  because those files aren't in the modern C# set (absence≠fix). Reconcile StigAnalyzer to classify
  Remediated (in-scope, genuinely fixed) vs Out-of-scope vs Residual; update demo console line + reference
  stig-after.json so "3 remediated" becomes "1 remediated / 2 out-of-scope / 2 residual" (matches Vol 2).
- 2026-05-31 — P5 complete: STIG-disposition + latent-defect-demo honesty fixes applied (commit 6533e27).
  Five adversarial auditors run (compliance, honesty, NAVAIR red-team, code/repro, security/ITAR). Fixes
  from findings (commit 6a83414, 84b68a1): corrected Chernoff bound (was 5.003e-7 mislabeled; now 95%
  rule-of-three upper bound 1.498e-3 = ln(20)/N, secondary 99.9% 3.454e-3); split equivalence-vs-latent
  framing in Vol 2; air-gap "replay not live-gen" wording; added scale/oracle-source para, performance
  gate, next-gen compat, surrogate-calibration honesty, KG strengthening; numbers 132→137 tests, 23→24
  projects; repo-relative paths (no username leak); NOTICE accuracy. Final consolidated re-audit:
  CLEAN FOR SUBMISSION (137 tests pass, Vol2 8pp ≤10, all numbers reconcile, no overclaims).
- 2026-05-31 — P6: RELEASE_APPROVAL.md added; repo publish-ready (security audit: safe as-is, clean-room,
  no sanitization needed). Held for H6 (PI push under own auth) + H7 (DSIP webforms + certify). Proposal
  in ../proposal/ (7 volumes + compliance matrix + DSIP guide); cited commit re-frozen at the push hash.
