#!/usr/bin/env node
/**
 * Live-mode bridge CLI — the process entrypoint the C# ProcessLiveModeBridge shells out to.
 *
 * Protocol (kept deliberately simple and documented so the seam is auditable):
 *   stdin  : JSON { mode: "local"|"cloud", request: LiveModeRequest }
 *   stdout : JSON TransformResult
 *   exit 0 : success;  non-zero : live runtime unavailable (the C# side surfaces this, never fakes)
 *
 * This CLI performs the ROUTING decision via @577-industries/model-router (selectModel). The actual
 * model CALL (Ollama for local, provider API for cloud) is intentionally left as the integration
 * point: without a reachable Ollama / API keys it exits non-zero rather than fabricate a transform.
 * The offline reviewer demo never runs this file.
 */

import { selectModel, type TaskFeatures } from "./route.js";

interface LiveModeRequest {
  taskId: string;
  unitId: string;
  sourceLanguage: string;
  targetStack: string;
  sovereignOnly: boolean;
  selectedAgentId: string;
  featureVector?: Record<string, number>;
  promptSha256: string;
}

function readStdin(): Promise<string> {
  return new Promise((resolve, reject) => {
    let data = "";
    process.stdin.setEncoding("utf8");
    process.stdin.on("data", (chunk) => (data += chunk));
    process.stdin.on("end", () => resolve(data));
    process.stdin.on("error", reject);
  });
}

async function main(): Promise<void> {
  const raw = await readStdin();
  const { mode, request } = JSON.parse(raw) as {
    mode: "local" | "cloud";
    request: LiveModeRequest;
  };

  const features = (request.featureVector ?? {}) as TaskFeatures;
  const selection = selectModel(features, { sovereignOnly: request.sovereignOnly });

  // Routing succeeded. The model CALL itself requires a live runtime we do not assume here.
  const liveRuntimeAvailable = false; // wired to an Ollama/provider client in a live deployment.
  if (!liveRuntimeAvailable) {
    const need =
      mode === "local"
        ? "a reachable Ollama runtime for sovereign/air-gapped execution"
        : "provider API keys for cloud execution";
    process.stderr.write(
      `model-router selected '${selection.modelId}' (${selection.strategy}, ` +
        `sovereign=${selection.isOpenSource}) but ${need} is not configured. ` +
        `Refusing to fabricate a transform; use Offline mode for the keyless demo.\n`,
    );
    process.exit(3);
    return;
  }

  // (Live deployment) — emit the produced TransformResult as JSON on stdout.
  const result = {
    taskId: request.taskId,
    files: [],
    agentId: selection.modelId,
    mode: mode === "local" ? "Local" : "Cloud",
    promptSha256: request.promptSha256,
    compiledClean: false,
    qualityEstimate: selection.score,
    notes: [
      `model-router strategy=${selection.strategy}`,
      `selected-model=${selection.modelId} (${selection.provider}, sovereign=${selection.isOpenSource})`,
      `requested-agent=${request.selectedAgentId}`,
    ],
  };
  process.stdout.write(JSON.stringify(result));
}

main().catch((err) => {
  process.stderr.write(`live-mode bridge error: ${err instanceof Error ? err.message : String(err)}\n`);
  process.exit(1);
});
