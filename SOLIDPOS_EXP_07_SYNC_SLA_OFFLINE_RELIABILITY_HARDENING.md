# SolidPOS EXP-07 — Sync SLA and Offline Reliability Hardening

## Status

PENDING USER VALIDATION

## Objective

Harden the sync SLA and offline reliability operating model after EXP-06. This phase validates that SolidPOS can continue limited production expansion with clear SLA thresholds, retry/dead-letter triage, conflict recovery, idempotency guarantees, and outbox/inbox reliability controls.

## Scope

- Sync SLA and offline reliability document pack.
- Runtime sync endpoint validation.
- SQL cross-check for inbox, changes, conflicts, duplicates, stale processing, pending retry, dead-letter, and cursor health.
- SLA owner/threshold/action matrix.
- Offline recovery and replay runbook.
- Dead-letter triage and manual retry policy.
- Idempotency and duplicate protection validation.
- GO/NO-GO decision toward EXP-08 Support and Incident Operations.

## Operational notes

EXP-07 is non-destructive. It does not create sales, inventory adjustments, stores, terminals, or cash movements. It reads production health and sync state, writes a local manifest/log, and validates SQL contract health.

## Expected decision

`GO_SYNC_SLA_OFFLINE_RELIABILITY_HARDENED` when blockers are empty.

## Known continuing conditions

- `monitor_retry_pending_sync`
- `dead_letter_sync_requires_triage` when dead-letter events exist
- `review_low_stock_threshold_coverage` from EXP-06 remains inventory-side and not blocking for EXP-07

## Next phase

EXP-08 — Support and Incident Operations
