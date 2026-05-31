#!/usr/bin/env bash
# gen-sbom.sh — generate a CycloneDX SBOM for the .NET solution into results/.
set -euo pipefail
cd "$(dirname "$0")/.."

DOTNET="${DOTNET:-$([ -x "$HOME/.dotnet/dotnet" ] && echo "$HOME/.dotnet/dotnet" || echo dotnet)}"
mkdir -p results/run

# The CycloneDX .NET tool is installed on demand into a local tool manifest.
if ! "$DOTNET" tool list --local 2>/dev/null | grep -qi cyclonedx; then
  "$DOTNET" new tool-manifest --force >/dev/null 2>&1 || true
  "$DOTNET" tool install CycloneDX >/dev/null 2>&1 || {
    echo "NOTE: CycloneDX tool unavailable offline; SBOM generation is exercised in CI/online runs."
    exit 0
  }
fi

"$DOTNET" CycloneDX ForgeEvolve.sln -o results/run -fn sbom.cdx.json -j
echo "==> SBOM written to results/run/sbom.cdx.json"
