# Human-in-the-Loop Review Gates

The government's topic Q&A states the expectation directly: *"a lot of human involvement until the
product is verified; then ECP to baseline and less human involvement."* FORGE EVOLVE for TMPC encodes
that as explicit, recorded gates. No transformation is accepted into the modern baseline without a
human sign-off carrying the rule-diff, the equivalence delta, and the STIG delta.

## Pipeline gates (per transformed component)
1. **Design gate** - after Discovery + Planning: human approves the proposed microservice boundary and
   the migration unit scope.
2. **Translation gate** - after Transformation: human reviews the emitted modern code, the extracted
   business rules it must honor, and the diff.
3. **Acceptance gate** - after Validation: human reviews the equivalence report (vectors passed,
   per-oracle deltas, intentional divergences) and the cATO deltas before the component is accepted.

Each gate produces a `ReviewGate` record (see `ForgeEvolve.Contracts`) appended to the tamper-evident
provenance chain.

## Program gates (human checkpoints for the PI / 577 Industries)
| Gate | When | Decision/action |
|---|---|---|
| H0 | After Phase 0 | Approve surrogate scope + frozen interface contracts |
| H1 | After surrogate build | Confirm surrogate is representative AND unmistakably synthetic |
| KG#1 | End of Month 3 (Base) | F1 ≥ 0.85 + oracle harness runs → continue full scope |
| KG#2 | End of Month 6 (Base) | 0 discrete violations + cATO bundle → recommend Option/Phase II |
| H-cost | Proposal cost volume | Supply real company/labor/rate numbers |
| H5 | After verification | Accept the all-auditors-pass package |
| H6 | Publish | Create/push the public repo under 577's GitHub auth |
| H7 | Submit | Complete DSIP webforms (FWA, foreign affiliations) + final certify |

Decisions are recorded here with date, gate id, outcome, and evidence pointer.
