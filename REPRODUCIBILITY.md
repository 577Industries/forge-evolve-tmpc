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

## Troubleshooting
| Symptom | Cause | Fix |
|---|---|---|
| `dotnet: command not found` / wrong SDK | .NET SDK 8 not on PATH | The repo pins `8.0.421` (rollForward `latestFeature`) in `global.json`. A user-local `~/.dotnet/dotnet` is auto-detected by the Makefile. Run `make tools` to confirm the detected versions. |
| `error NETSDK1045` / SDK version mismatch | Installed SDK older than the `global.json` pin | Install .NET SDK 8.0.421+, or rely on `~/.dotnet/dotnet`; `rollForward: latestFeature` accepts newer 8.0.x patches. |
| Node errors in `orchestrator/` | Node too old | Node ≥ 18 required (CI uses 20). Check with `node --version`. |
| `docker: command not found` | Docker missing | Docker is only needed for the **legacy surrogate runtime** used in differential testing. The default offline replay path (`make demo`) needs **no Docker**. |
| `make verify` reports a hash mismatch | Pipeline not running deterministically | Ensure `FORGE_ORCHESTRATOR_MODE=offline` (the default). Live `cloud`/`local` modes are not byte-deterministic. On Windows, run targets from **Git Bash**. |
| Demo asks for an API key | Orchestrator running in a live mode | Confirm offline mode (`FORGE_ORCHESTRATOR_MODE=offline`); the offline transcript-replay path needs no keys and no network. |
