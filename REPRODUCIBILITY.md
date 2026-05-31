# Reproducibility

This repository is built so a reviewer can reproduce every cited metric from a clean clone, **offline,
with no API keys**.

## Toolchain (pinned)
| Tool | Version | Pin |
|---|---|---|
| .NET SDK | 8.0.x | `global.json` (`8.0.421`, rollForward latestFeature) |
| Node.js | ≥ 18 | `orchestrator/.nvmrc` (Phase 2) |
| Python | ≥ 3.10 | corpus generation only |
| Docker | any recent | legacy surrogate runtime (differential testing) |

## One-command reproduction
```bash
git clone https://github.com/577Industries/forge-evolve-tmpc
cd forge-evolve-tmpc
make demo      # offline; writes results/run/
make verify    # runs the demo twice; asserts identical output hashes
make audit     # validates the claim->evidence matrix
```

## Determinism
The demo runs the Tool Orchestrator in `offline` mode: model interactions are served from a recorded
**transcript cache** keyed by a hash of (prompt, model, task). This makes the pipeline deterministic by
construction — `make verify` confirms two runs produce byte-identical evidence. Live `cloud`/`local`
model runs may vary; only the offline path backs the reproducible headline numbers.

## Submission anchoring
At proposal submission, the proposal cites this repository at a specific **commit SHA**, and any
published submodule artifacts at release tags. Reviewers verify with:
```bash
git rev-parse HEAD                 # must equal the SHA cited in the proposal
git submodule status               # (post-publish) must match the pinned tags
```
Expected toolchain, the frozen corpus tag, and the committed reference run live in `results/reference/`.
