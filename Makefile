# FORGE EVOLVE for TMPC — developer & reviewer entrypoints.
# All targets are designed to run OFFLINE with no API keys.

# Prefer a user-local .NET SDK install if present, else the one on PATH.
DOTNET := $(shell [ -x "$$HOME/.dotnet/dotnet" ] && echo "$$HOME/.dotnet/dotnet" || echo dotnet)
export DOTNET_CLI_TELEMETRY_OPTOUT := 1
export DOTNET_NOLOGO := 1
# Default to the deterministic, keyless replay mode for the demo.
export FORGE_ORCHESTRATOR_MODE ?= offline

.DEFAULT_GOAL := help

.PHONY: help build test demo verify audit sbom corpus clean tools

help: ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN{FS=":.*?## "}{printf "  \033[36m%-10s\033[0m %s\n", $$1, $$2}'

tools: ## Print detected toolchain versions
	@echo "dotnet: $$($(DOTNET) --version 2>/dev/null || echo MISSING)"
	@echo "node:   $$(node --version 2>/dev/null || echo MISSING)"
	@echo "python: $$(python3 --version 2>/dev/null || echo MISSING)"

build: ## Build the .NET solution (and the TS orchestrator if present)
	$(DOTNET) build ForgeEvolve.sln -v q
	@if [ -f orchestrator/package.json ]; then cd orchestrator && npm run build --if-present; fi

test: ## Run unit + integration tests with coverage
	$(DOTNET) test ForgeEvolve.sln --collect:"XPlat Code Coverage" -v q

corpus: ## (Re)generate the synthetic golden test corpus (fixed seed)
	python3 surrogate/corpus/generate.py

demo: ## Run the full modernization pipeline on the surrogate (offline, no keys)
	@bash scripts/run-demo.sh

verify: ## Re-run the demo and confirm deterministic (identical output hashes)
	@bash scripts/verify-reproducible.sh

audit: ## Validate the Claim->Evidence Traceability Matrix (honesty gate)
	node evidence/validate-cetm.mjs

sbom: ## Generate a CycloneDX SBOM for the solution
	@bash scripts/gen-sbom.sh

clean: ## Remove build outputs and local demo run
	$(DOTNET) clean ForgeEvolve.sln -v q || true
	rm -rf results/run
	find . -type d \( -name bin -o -name obj -o -name node_modules \) -prune -exec rm -rf {} + 2>/dev/null || true
