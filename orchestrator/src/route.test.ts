import { describe, expect, it } from "vitest";
import {
  deriveCapabilities,
  deriveStrategy,
  selectModel,
  type TaskFeatures,
} from "./route.js";

/**
 * These tests need NO network and NO API key: @577-industries/model-router is pure selection logic
 * over an in-memory model registry. That is exactly why the orchestrator can prove the live-mode
 * reuse without any provider credentials.
 */
describe("live-mode bridge: model-router reuse", () => {
  const complexTask: TaskFeatures = { complexity: 0.9, contextSize: 0.8, crypto: 0.6 };

  it("selects a SOVEREIGN (open-source) model when sovereignOnly=true", () => {
    const selection = selectModel(complexTask, { sovereignOnly: true });

    // The load-bearing air-gap assertion: an air-gapped deployment must never get a hosted model.
    expect(selection.sovereignOnly).toBe(true);
    expect(selection.isOpenSource).toBe(true);
    expect(selection.strategy).toBe("efficiency");
    expect(selection.modelId).toBeTruthy();
  });

  it("may select a hosted (non-sovereign) model when sovereignOnly is not set", () => {
    const selection = selectModel(complexTask, { sovereignOnly: false });
    expect(selection.sovereignOnly).toBe(false);
    // High complexity/context → capability strategy.
    expect(selection.strategy).toBe("capability");
    expect(selection.modelId).toBeTruthy();
  });

  it("derives the efficiency strategy in air-gapped mode regardless of features", () => {
    expect(deriveStrategy({ complexity: 0.9 }, true)).toBe("efficiency");
    expect(deriveStrategy({ costSensitivity: 0.9 }, true)).toBe("efficiency");
  });

  it("derives cost strategy for cost-sensitive cloud tasks", () => {
    expect(deriveStrategy({ costSensitivity: 0.8 }, false)).toBe("cost");
  });

  it("always requires the 'code' capability and adds 'analysis' for crypto-sensitive cloud tasks", () => {
    expect(deriveCapabilities({})).toEqual(["code"]);
    expect(deriveCapabilities({ crypto: 0.7 })).toEqual(["code", "analysis"]);
    // In sovereign mode 'analysis' is dropped (no open-source model advertises it) so routing
    // never collapses to zero candidates in an air-gap.
    expect(deriveCapabilities({ crypto: 0.7 }, true)).toEqual(["code"]);
  });

  it("routes a crypto-sensitive task to a sovereign model in air-gapped mode (no dead-end)", () => {
    const selection = selectModel({ crypto: 0.9, complexity: 0.8 }, { sovereignOnly: true });
    expect(selection.isOpenSource).toBe(true);
    expect(selection.modelId).toBeTruthy();
  });

  it("sovereign selection is deterministic and repeatable", () => {
    const a = selectModel(complexTask, { sovereignOnly: true });
    const b = selectModel(complexTask, { sovereignOnly: true });
    expect(a.modelId).toBe(b.modelId);
  });
});
