# EXP-07 — Idempotency and Duplicate Protection

## Contract

Sync must be idempotent by tenant, terminal, event id, batch id, and sequence number. Duplicate push attempts must not create duplicate sales, payments, cash movements, receipts, or inventory movements.

## Required guards

- `sync_inbox_events` unique event identity.
- Batch/sequence grouping for replay order.
- Duplicate status handling.
- Payload hash retained for diagnostics.
- Replay must preserve original event id and local occurrence time.

## SQL validation

EXP-07 checks duplicate batch/sequence candidates, invalid sync statuses, stale processing events, retry pending events without next retry, pending conflicts, and dead-letter visibility.
