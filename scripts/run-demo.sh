#!/usr/bin/env bash
# run-demo.sh — drives the full FORGE EVOLVE for TMPC pipeline on the synthetic surrogate.
# Offline and keyless by default (FORGE_ORCHESTRATOR_MODE=offline).
set -euo pipefail
cd "$(dirname "$0")/.."

DOTNET="${DOTNET:-$([ -x "$HOME/.dotnet/dotnet" ] && echo "$HOME/.dotnet/dotnet" || echo dotnet)}"
RUN_DIR="results/run"
CLI_PROJ="src/ForgeEvolve.Cli/ForgeEvolve.Cli.csproj"

echo "==> FORGE EVOLVE for TMPC — demo (mode=${FORGE_ORCHESTRATOR_MODE:-offline})"

if [ ! -f "$CLI_PROJ" ]; then
  echo "NOTE: the CLI driver ($CLI_PROJ) is assembled during the integration phase (P3)."
  echo "      Until then, individual stage tests run via 'make test'."
  exit 0
fi

mkdir -p "$RUN_DIR"
"$DOTNET" run --project "$CLI_PROJ" -c Release -- \
  --surrogate surrogate/tmpc-surrogate-mds \
  --out "$RUN_DIR" \
  --mode "${FORGE_ORCHESTRATOR_MODE:-offline}"

echo "==> Demo complete. Evidence written to $RUN_DIR/"
