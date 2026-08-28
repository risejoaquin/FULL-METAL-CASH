# EXP-07 — Sync SLA and Offline Reliability Hardening

## Objective

Harden sync SLA and offline reliability for limited production expansion.

## Scope

EXP-07 validates runtime and database evidence for:

- sync SLA
- offline recovery
- retry pending monitoring
- dead-letter triage
- conflict recovery
- idempotency
- duplicate protection
- outbox/inbox reliability
- replay safety
- schema version 4 compatibility

## Non-destructive phase

EXP-07 does not modify sales, receipts, cash shifts, stores, terminals, inventory, or product catalog data. It validates existing production state and produces local evidence.

## Acceptance

The phase is GO only when there are no SQL blocking reasons and the API/runtime contract is reachable. Warnings are allowed only when they have an owner, threshold, action, and escalation path.

## Next phase

EXP-08 — Support and Incident Operations
