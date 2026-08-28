# LGA-11 — Limited GA Public GA Readiness Reassessment

## Objective
Reassess whether SolidPOS may be considered for Public GA after LGA-10 commercial operations confidence passed, without activating Public GA in this phase.

## Entry gate
- LGA-10 must be PASS and reviewed.
- Limited GA remains the active rollout mode.
- The capacity finding from LGA-09/LGA-10 must be remeasured, not ignored.

## Public GA readiness reassessment scope
LGA-11 validates health live/ready, concurrency 3, p95 readiness, Railway capacity evidence, DB pressure, waiting connections, stores and terminals, commercial operations, cash shifts, inventory, dashboard, reports, audit, support operations, sync queues, RLS and schema version 4.

## Mandatory invariants
- `schemaVersion = 4` and `schema_version_4` remain authoritative.
- Public GA NOT ACTIVATED.
- `PublicGaDecision = KEEP_LIMITED_GA` is the only activation-state decision allowed by this validator.
- Negative stock count remains 0.
- Waiting connections baseline remains <= 12 and cannot be silently raised.
- Existing conflict/dead-letter baselines cannot increase silently.
- Limited GA remains capped at 2 active stores.
- Commercial operation evidence from LGA-10 must remain healthy.

## Capacity decision contract
If the concurrency-3 probe fails or either health probe exceeds the configured p95 threshold, the validator requires:

`ReadinessDecision = KEEP_LIMITED_GA`

`CapacityRecommendation = CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA`

If the capacity probe passes, `CapacityRecommendation = CAPACITY_GATE_PASSED` is required. A passing capacity gate may allow `RECOMMEND_PUBLIC_GA_REVIEW`, but it still does not activate Public GA.

## Expected current decision
With Railway still constrained, the expected result is:

`KEEP_LIMITED_GA`

`PUBLIC GA NOT ACTIVATED`

`CAPACITY UPGRADE REQUIRED BEFORE PUBLIC GA`

Decision wording: KEEP LIMITED GA.
