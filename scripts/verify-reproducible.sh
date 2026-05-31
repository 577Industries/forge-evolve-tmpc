#!/usr/bin/env bash
# verify-reproducible.sh — runs the demo twice and asserts byte-identical evidence (determinism).
# This is what backs the "pinned-hash reproducibility" claim. Offline mode only.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ ! -f "src/ForgeEvolve.Cli/ForgeEvolve.Cli.csproj" ]; then
  echo "NOTE: reproducibility check is enabled once the pipeline CLI is assembled (P3)."
  exit 0
fi

hash_run() {
  find results/run -type f \( -name '*.json' -o -name '*.ttl' -o -name '*.yaml' -o -name '*.csv' \) \
    -not -name 'provenance.json' | sort | xargs sha256sum | sha256sum | awk '{print $1}'
}

rm -rf results/run
FORGE_ORCHESTRATOR_MODE=offline bash scripts/run-demo.sh >/dev/null
H1="$(hash_run)"
rm -rf results/run
FORGE_ORCHESTRATOR_MODE=offline bash scripts/run-demo.sh >/dev/null
H2="$(hash_run)"

echo "run #1: $H1"
echo "run #2: $H2"
if [ "$H1" = "$H2" ]; then
  echo "PASS: demo output is deterministic (identical across runs)."
else
  echo "FAIL: demo output differs across runs — not deterministic."
  exit 1
fi
