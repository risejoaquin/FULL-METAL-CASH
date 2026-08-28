# SolidPOS — LGA-11 Limited GA Public GA Readiness Reassessment

## Objective
Reassess Public GA readiness after LGA-10 without activating Public GA.

## Modules affected
- PosServer/API validation surface
- PostgreSQL production read-only validation
- PosCore WPF guardrails
- PosDashboard operational verification
- GA support/on-call documentation

No application business-code behavior is changed. LGA-11 adds validation, evidence and decision contracts.

## Technical decision
LGA-11 separates readiness recommendation from activation. `PublicGaDecision` is locked to `KEEP_LIMITED_GA`. Capacity is measured at concurrency 3 with the existing 1200 ms p95 target.

When the capacity probe fails, PASS requires `KEEP_LIMITED_GA` and `CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA`. When it passes, the manifest must record `CAPACITY_GATE_PASSED`; any Public GA review recommendation remains non-activating and requires separate review.

## Risk controls
- no negative-stock baseline relaxation;
- no waiting-connection baseline increase;
- no schema version change;
- no sync conflict/dead-letter baseline increase;
- no silent rollout expansion;
- no Public GA flag mutation.

## Expected current production result

`PASS LGA-11 LIMITED GA PUBLIC GA READINESS REASSESSMENT / KEEP LIMITED GA / PUBLIC GA NOT ACTIVATED`

A PASS authorizes review for LGA-12 only. It does not authorize Public GA.
