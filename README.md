# FORGE EVOLVE for TMPC

[![demo-offline](https://github.com/577Industries/forge-evolve-tmpc/actions/workflows/demo-offline.yml/badge.svg)](https://github.com/577Industries/forge-evolve-tmpc/actions/workflows/demo-offline.yml)
[![License: Apache-2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](global.json)
[![tests](https://img.shields.io/badge/tests-137_passing-brightgreen.svg)](#build--test-developers)
[![make demo](https://img.shields.io/badge/make_demo-offline_%7C_keyless_%7C_deterministic-success.svg)](#-for-navy-sbir-reviewers--5-minute-onramp)

**AI-assisted modernization of C#/.NET mission-planning software, with behavioral-equivalence proof
and continuous-ATO artifacts — runnable end-to-end on a synthetic, unclassified surrogate.**

Developed by [577 Industries, Inc.](https://github.com/577Industries) under U.S. Navy SBIR topic
**DON26BZ01-NV013** ("AI-Assisted Modernization and Optimization of Theater Mission Planning Center
(TMPC) Software"). This repository is the **C#/.NET/JavaScript/SQL extension** of the FORGE EVOLVE
code-modernization framework. *(FORGE EVOLVE's prior published results cover COBOL/Fortran/Ada/VB6 →
Java/Python/Rust/TypeScript; the C#/.NET capability here is newly developed for the TMPC stack.)*

---

## ⭐ For Navy SBIR Reviewers — 5-minute onramp

This repo lets you **run the modernization pipeline yourself**, with **no API keys and no network**,
and inspect both the legacy input and the modernized output.

```bash
git clone https://github.com/577Industries/forge-evolve-tmpc
cd forge-evolve-tmpc
make demo        # offline, deterministic, ~minutes
```

`make demo` runs the full pipeline on the synthetic surrogate and writes evidence to `results/`:

| Phase-I ask (topic) | What the demo does | Evidence produced |
|---|---|---|
| 1. Analyze architecture, identify modernization needs | Roslyn/Tree-sitter discovery → dependency graph, complexity, business rules, crypto inventory | `results/discovery-report.json`, `results/business-rules.ttl` |
| 2. Conceptual AI models for analysis/refactor/cyber | CLAR lift → risk-scored microservice decomposition → multi-agent transform | `results/migration-plan.json`, `results/clar/*.jsonld` |
| 3. PoC AI refactoring on a representative component | Modernize a coupled C# component to .NET 8; prove behavioral equivalence | `results/equivalence-report.json`, `tmpc-modern-mds/` |
| 4. Phase II prototype plans + cyber | cATO artifacts as CI byproducts | `results/stig-findings.*.json`, `results/control-map.yaml`, `results/sbom.cdx.json`, `results/provenance.json`, `results/poam.csv` |

**What to read first:** [`companion/companion.md`](companion/companion.md) maps each proposal claim to the
exact file/test that backs it. [`EXCLUSIONS.md`](EXCLUSIONS.md) states why this is safe to publish.

> **Honesty note.** Every metric the demo prints is measured on the **synthetic, unclassified**
> surrogate in [`surrogate/`](surrogate/) and is **preliminary** — not validated against government
> TMPC code and not validated by the Navy. The surrogate is engineered to exhibit the *same classes*
> of technical debt and *shapes* of mission-planning logic the government described, so results
> transfer in kind, not in fidelity. See [`surrogate/DEBT.md`](surrogate/DEBT.md).

## Reading order for first-time visitors

1. [`companion/companion.md`](companion/companion.md) — each proposal claim mapped to the exact file/test that backs it.
2. [`REPRODUCIBILITY.md`](REPRODUCIBILITY.md) — reproduce every cited metric offline, from a clean clone.
3. [`EXCLUSIONS.md`](EXCLUSIONS.md) — why this is unclassified, ITAR-clean, and safe to publish.
4. [`governance/pre-registration.md`](governance/pre-registration.md) — metrics, thresholds, and kill-gates frozen before any run.
5. Companion site: <https://577industries.github.io/forge-evolve-tmpc/>

---

## What it does (the pipeline)

```
ingest → Discovery → CLAR → Migration Planner → Tool Orchestrator → Transformation
       → Validation (behavioral equivalence) → Cyber/cATO overlay → Governance
```

- **Discovery** — [Roslyn](https://github.com/dotnet/roslyn) semantic analysis of C# (+ Tree-sitter for
  JS/SQL, FORGE VB6 grammar): dependency graph, Tarjan SCCs, complexity vectors, business-rule
  extraction (AST + LLM majority vote → RDF), cryptographic inventory.
- **CLAR** — Cross-Language Abstract Representation ([`clar-spec/`](clar-spec/CLAR.schema.json)):
  four layers, decouples source parsing from target generation; flags precision-constrained values so
  coordinate/time-on-target math is emitted with `decimal`/checked arithmetic.
- **Migration Planner** — composite risk scoring + spectral clustering → candidate microservice
  boundaries + a topologically-ordered, risk-scored migration sequence (the Phase II roadmap).
- **Tool Orchestrator** — routes transform tasks via
  [`@577-industries/model-router`](https://github.com/577Industries); runs **offline** (deterministic
  transcript replay — the default), **local** (sovereign/air-gapped), or **cloud**.
- **Transformation** — emits modern, testable .NET 8 with secure-by-construction patterns.
- **Validation** — differential + property-based testing with **mission-data-aware oracles**
  (route feasibility, anti-meridian, time-on-target); discrete outputs to exact equality, continuous to
  bounded relative error; reports a Chernoff confidence bound.
- **Cyber/cATO** — STIG checks (before/after), NIST 800-53 mapping, CycloneDX SBOM, hashchain
  provenance ([`@577-industries/hashchain-audit`](https://github.com/577Industries)), POA&M.
- **Governance** — human-in-the-loop review gates + tamper-evident audit trail.

## Layout

| Path | Contents |
|---|---|
| [`src/`](src/) | The FORGE EVOLVE for TMPC engine (.NET). `ForgeEvolve.Contracts` = frozen interfaces/DTOs. |
| [`orchestrator/`](orchestrator/) | TypeScript orchestration layer (consumes `@577-industries/*`). |
| [`surrogate/`](surrogate/) | The synthetic, unclassified MDS-like component + golden corpus + legacy runtime. |
| [`clar-spec/`](clar-spec/) | CLAR JSON-LD schema + per-language profiles. |
| [`evidence/`](evidence/) | Claim→Evidence Traceability Matrix (CETM) + validator. |
| [`results/`](results/) | Demo outputs (a committed reference run + your local run). |
| [`governance/`](governance/) | Pre-registration of metrics + human review gates. |

## Build / test (developers)

```bash
make build      # dotnet build + npm build
make test       # unit + integration tests with coverage
make audit      # run the CETM validator (claim→evidence honesty check)
make sbom       # generate the CycloneDX SBOM
```

Toolchain: .NET SDK 8 (pinned in `global.json`), Node 18+, Python 3.10+ (corpus generation only),
Docker (legacy surrogate runtime for differential testing). See [`REPRODUCIBILITY.md`](REPRODUCIBILITY.md).

## License
Apache-2.0 — see [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE).
