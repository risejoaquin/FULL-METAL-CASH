# EXP-07 — Offline Recovery Runbook

## Goal

Recover terminals that were offline without duplicate sales, duplicate payments, or cross-store leakage.

## Recovery sequence

1. Confirm health/live and health/ready.
2. Confirm terminal identity and store assignment.
3. Check local outbox count before reconnect.
4. Push batches in order.
5. Verify idempotency by batch id, event id, and sequence number.
6. Pull server changes after push.
7. Confirm pending conflicts are zero.
8. Confirm dead-letter count for that terminal is zero or triaged.
9. Confirm dashboard metrics reflect processed events.

## GO criteria

- No stale processing events.
- No duplicate batch/sequence violations.
- No pending conflicts.
- Dead-letter events are triaged.
- Retry pending events are monitored and have next_retry_at.
