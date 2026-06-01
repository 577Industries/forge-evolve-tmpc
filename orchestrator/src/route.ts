/**
 * FORGE EVOLVE for TMPC — TypeScript live-mode bridge.
 *
 * This module is the "already-built reuse" proof: the Tool Orchestrator's Local (sovereign /
 * air-gapped) and Cloud modes select a model through the published @577-industries/model-router
 * package — the same router shipped in FORGE OS. It is NOT exercised by the offline reviewer demo
 * (that path is pure C# transcript replay with no Node dependency).
 *
 * Given a transform-task feature vector, `selectModel()` maps the task to a routing strategy and a
 * set of required capabilities, then asks the 577 router for the optimal model. When
 * `sovereignOnly` is true (Local / air-gapped mode) the router is constrained to open-source models
 * only, so an air-gapped deployment can never select a hosted provider.
 */

import { createRouter } from "@577-industries/model-router";
import type {
  ModelCapability,
  RouteResult,
  RoutingStrategy,
} from "@577-industries/model-router";

/** A transform task's feature vector, mirroring the C# TransformTask.FeatureVector. */
export interface TaskFeatures {
  /** Cyclomatic / structural complexity, normalized ~[0,1]. */
  complexity?: number;
  /** Cryptographic-sensitivity signal, ~[0,1]. */
  crypto?: number;
  /** Context-size pressure (LOC / token budget), ~[0,1]. */
  contextSize?: number;
  /** Mission-routing-logic density, ~[0,1]. */
  routing?: number;
  /** Cost sensitivity of this task, ~[0,1]; high → prefer the cost strategy. */
  costSensitivity?: number;
  [dimension: string]: number | undefined;
}

/** Input to the live-mode bridge. Mirrors the C# LiveModeRequest payload. */
export interface RouteOptions {
  /** Air-gapped / Local mode: constrain to sovereign (open-source) models only. */
  sovereignOnly?: boolean;
  /** Override the auto-derived strategy (otherwise inferred from the feature vector). */
  strategy?: RoutingStrategy;
  /** Extra required capabilities beyond those derived from the features. */
  requireCapabilities?: ModelCapability[];
}

/** The orchestrator's view of a routing decision. */
export interface ModelSelection {
  modelId: string;
  displayName: string;
  provider: string;
  /** Strategy actually used for the decision. */
  strategy: RoutingStrategy;
  /** True when the chosen model is open-source (the only kind allowed under sovereignOnly). */
  isOpenSource: boolean;
  /** True when the router classified the model as an SLM. */
  isSlm: boolean;
  /** Composite score from the router. */
  score: number;
  /** Whether sovereign filtering was applied. */
  sovereignOnly: boolean;
}

const CODE_CAPABILITY: ModelCapability = "code";

/**
 * Derive the routing strategy from the task feature vector.
 *  - air-gapped/local tasks favor `efficiency` (best capability-per-cost with the open-source bonus),
 *  - cost-sensitive tasks use `cost`,
 *  - high-complexity / large-context tasks use `capability`,
 *  - everything else uses the router's `balanced` default.
 */
export function deriveStrategy(features: TaskFeatures, sovereignOnly: boolean): RoutingStrategy {
  if (sovereignOnly) return "efficiency";
  if ((features.costSensitivity ?? 0) >= 0.7) return "cost";
  if ((features.complexity ?? 0) >= 0.7 || (features.contextSize ?? 0) >= 0.7) return "capability";
  return "balanced";
}

/**
 * Derive required capabilities from the feature vector. Transform tasks always need "code"; a
 * crypto-sensitive task additionally prefers "analysis".
 *
 * In sovereign/air-gapped mode "analysis" is intentionally NOT hard-required: no open-source model
 * in the 577 registry advertises it, and an air-gapped deployment must still be able to route. We
 * therefore keep "code" as the hard requirement (DeepSeek V3 and Mistral Large satisfy it) and let
 * crypto-sensitivity steer strategy instead of eliminating every candidate.
 */
export function deriveCapabilities(
  features: TaskFeatures,
  sovereignOnly = false,
): ModelCapability[] {
  const caps: ModelCapability[] = [CODE_CAPABILITY];
  if (!sovereignOnly && (features.crypto ?? 0) >= 0.5) caps.push("analysis");
  return caps;
}

/**
 * Select a model for a transform task using the 577 model-router.
 *
 * The offline C# path never calls this; the C# orchestrator invokes it (via the process bridge)
 * only in Local/Cloud mode. The returned selection is what the bridge would then drive (Ollama for
 * sovereign/local, the provider API for cloud).
 */
export function selectModel(features: TaskFeatures, options: RouteOptions = {}): ModelSelection {
  const sovereignOnly = options.sovereignOnly ?? false;
  const strategy = options.strategy ?? deriveStrategy(features, sovereignOnly);
  const capabilities = [
    ...deriveCapabilities(features, sovereignOnly),
    ...(options.requireCapabilities ?? []),
  ];

  // The real, already-built 577 router. Zero runtime deps, no API keys, no network — it is pure
  // selection logic over a model registry, which is why it is safe to exercise in a unit test.
  const router = createRouter({ strategy });

  const result: RouteResult = router.route({
    sovereignOnly,
    strategy,
    capabilities,
  });

  return {
    modelId: result.model.id,
    displayName: result.model.displayName,
    provider: result.model.provider,
    strategy,
    isOpenSource: result.model.isOpenSource === true,
    isSlm: result.isSlm,
    score: result.score,
    sovereignOnly,
  };
}
