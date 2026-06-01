# Release Approval — Public Publication Gate

This repository is **publish-ready** but is held **pending PI authorization** (gate H6). It will be
pushed to **github.com/577Industries/forge-evolve-tmpc** under 577's own GitHub credentials. Agents
prepare the release; the Principal Investigator authorizes and executes the push.

## What is published
The entire repository, Apache-2.0. It is a **clean-room** TMPC-reference implementation containing
**no** 577 proprietary FORGE EVOLVE / FORGE OS engine internals, **no** model weights, and **no**
real/controlled TMPC data — so there is nothing to sanitize or split out. See `EXCLUSIONS.md`.

## Pre-publication verification (all PASS at the frozen commit)
- [x] `make build` — solution builds, 0 warnings / 0 errors (24 projects)
- [x] `make test` — 137 unit tests pass, 0 failures
- [x] `make demo` — runs offline, no API keys, end-to-end
- [x] `make verify` — byte-deterministic across runs
- [x] `make audit` — CETM `issues_count: 0`
- [x] Independent security/ITAR audit — **no secrets, no real/controlled data, Apache-2.0 clean, safe to publish**
- [x] No absolute build paths / usernames in committed artifacts (recursive username/home-path grep over `results/` → clean)
- [x] CI workflow (`.github/workflows/demo-offline.yml`) runs build + test + offline demo + secret scan on push

## Reviewer reproduction contract (what a Navy reviewer does)
```bash
git clone https://github.com/577Industries/forge-evolve-tmpc
cd forge-evolve-tmpc            # checkout the commit cited in the proposal
make demo                       # offline, no keys; prints the headline metrics
make verify                     # confirms byte-determinism
make test                       # 137 passing tests
```
Prerequisite: .NET SDK 8 (pinned in `global.json`), Node 18+. See `REPRODUCIBILITY.md`.

## Honest scope of claims (re-affirmed at release)
All quantitative results are measured on the **synthetic, unclassified surrogate**, are **preliminary**,
and are **not government-validated**. FORGE EVOLVE's prior published benchmarks are internal/preliminary
prior art for COBOL/Fortran/Ada/VB6 → Java/Python/Rust/TS; the C#/.NET/JS/SQL capability here is the
newly developed extension. CMMC L2 (Self), ITAR/DDTC registration, and any facility clearance are
in-progress / at-award.

## PI authorization (H6)
- [ ] Frozen submission commit: `__________________` (full SHA — set at push)
- [ ] Tag pushed: `v0.1.0-sbir-DON26BZ01-NV013`
- [ ] Proposal commit-hash citations updated to the frozen full SHA (Vol 2 header, §a/§d/§g/§l; Vol 1; Vol 3; Vol 5; guide)
- [ ] Repo set **public** on github.com/577Industries
- [ ] Approved by: Thomas Waweru, Ph.D. (PI) — date: __________
