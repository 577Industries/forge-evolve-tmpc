#!/usr/bin/env node
// validate-cetm.mjs — validates the Claim → Evidence Traceability Matrix (CETM).
//
// Every proposal claim must carry a status of A, E, or P:
//   A (Anchored)      — resolves to a runnable artifact in this repo. evidence_artifact MUST
//                       point at a file that exists on disk; verification_command SHOULD be set.
//   E (External)      — resolves to a cited external source. evidence_artifact = the citation.
//   P (Preliminary)   — explicitly internal/preliminary/surrogate. MUST carry a non-empty `label`
//                       string that is the exact in-text disclaimer required at the claim site.
// Status "I" (implicit / unanchored) or any other value is FORBIDDEN.
//
// Honesty rule of record: a FORGE EVOLVE benchmark or any capability number is P by construction
// until a runnable artifact in THIS repo reproduces it on the surrogate, at which point it may
// become A (anchored to the surrogate, never to real TMPC code).
//
// Usage:  node evidence/validate-cetm.mjs [<path-to-cetm.json>]
// Exit 0 only when issues_count == 0 and no forbidden statuses.

import { readFileSync, existsSync, statSync } from "node:fs";
import { dirname, isAbsolute, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = join(SCRIPT_DIR, "..");
const DEFAULT_CETM = join(SCRIPT_DIR, "cetm.json");

const VALID_STATUSES = new Set(["A", "E", "P"]);
const VALID_TYPES = new Set(["quantitative", "capability", "compliance"]);
const CLAIM_ID_PATTERN = /^C-[A-Z0-9][A-Z0-9-]*$/u;
// In-package prefixes whose paths must resolve on disk for status-A rows.
const IN_PACKAGE_PREFIXES = ["src/", "surrogate/", "clar-spec/", "evidence/", "results/", "orchestrator/", "tests/", "companion/", "governance/", "proposal/"];

function fail(msg) { console.error(`FAIL: ${msg}`); process.exit(1); }

function cleanArtifactPath(raw) {
  let p = String(raw).split("#")[0].split("?")[0];
  const paren = p.indexOf(" (");
  if (paren !== -1) p = p.slice(0, paren);
  const lastColon = p.lastIndexOf(":");
  if (lastColon !== -1) {
    const tail = p.slice(lastColon + 1);
    if (!tail.includes("/") && /^(?:\d+(?:-\d+)?|§[\w.\-]+|[A-Za-z_]+\(\))/u.test(tail)) {
      p = p.slice(0, lastColon);
    }
  }
  return p.trim();
}

const isInPackagePath = (p) => !!p && !isAbsolute(p) && IN_PACKAGE_PREFIXES.some((x) => p.startsWith(x));

function pathExists(p) {
  let c = p.endsWith("/*") || p.endsWith("/") ? p.replace(/\/?\*?$/, "") : p;
  if (!c) return false;
  try { statSync(isAbsolute(c) ? c : join(REPO_ROOT, c)); return true; } catch { return false; }
}

function main() {
  const cetmPath = process.argv[2] || DEFAULT_CETM;
  if (!existsSync(cetmPath)) fail(`CETM file not found at ${cetmPath}`);
  let cetm;
  try { cetm = JSON.parse(readFileSync(cetmPath, "utf8")); }
  catch (e) { fail(`Could not parse CETM JSON: ${e.message}`); }
  if (!Array.isArray(cetm.claims)) fail("CETM JSON missing top-level 'claims' array");

  const issues = [];
  const counts = { A: 0, E: 0, P: 0, other: 0 };
  const seen = new Set();

  for (const row of cetm.claims) {
    const id = row.claim_id || "<no claim_id>";
    if (!row.claim_id) { issues.push(`Row missing claim_id: ${JSON.stringify(row).slice(0, 100)}`); continue; }
    if (seen.has(row.claim_id)) issues.push(`${id} duplicate claim_id`);
    seen.add(row.claim_id);
    if (!CLAIM_ID_PATTERN.test(row.claim_id)) issues.push(`${id} claim_id must match /^C-[A-Z0-9][A-Z0-9-]*$/`);
    if (!row.claim_text) issues.push(`${id} missing claim_text`);
    if (!row.source_location) issues.push(`${id} missing source_location (volume/section)`);
    if (row.claim_type && !VALID_TYPES.has(row.claim_type))
      issues.push(`${id} claim_type="${row.claim_type}" must be one of ${[...VALID_TYPES].join(", ")}`);

    if (!row.status) { issues.push(`${id} missing status`); counts.other++; continue; }
    if (!VALID_STATUSES.has(row.status)) {
      issues.push(`${id} status="${row.status}" — must be A, E, or P (status I/other is forbidden)`);
      counts.other++; continue;
    }
    counts[row.status]++;

    if (row.status === "P") {
      if (!row.label || String(row.label).trim().length === 0)
        issues.push(`${id} status=P but missing required in-text disclaimer 'label' (e.g. "preliminary; surrogate-based; not government-validated")`);
    }

    if ("evidence_hash" in row && row.evidence_hash !== null && typeof row.evidence_hash !== "string")
      issues.push(`${id} evidence_hash must be null or a string`);

    if ("verification_command" in row && row.verification_command !== null &&
        (typeof row.verification_command !== "string" || row.verification_command.trim() === ""))
      issues.push(`${id} verification_command must be null or a non-empty string`);

    if (row.evidence_artifact) {
      const parts = String(row.evidence_artifact).split(/\s*\+\s*/).map(cleanArtifactPath).filter(Boolean);
      for (const part of parts) {
        if (row.status === "A" && isInPackagePath(part) && !pathExists(part))
          issues.push(`${id} status=A but evidence_artifact '${part}' does not exist on disk (downgrade to E until the artifact lands)`);
      }
    } else if (row.status === "A") {
      issues.push(`${id} status=A but no evidence_artifact pointer`);
    }
  }

  const summary = {
    cetm_path: relative(REPO_ROOT, cetmPath),
    total_claims: cetm.claims.length,
    counts,
    issues_count: issues.length,
  };
  console.log(JSON.stringify(summary, null, 2));
  if (issues.length > 0 || counts.other > 0) {
    console.error(`\nValidation issues (${issues.length}):`);
    for (const i of issues) console.error(`  - ${i}`);
    process.exit(1);
  }
}

main();
