# EXP-07 — Dead-letter Triage Runbook

## Purpose

Classify and recover sync dead-letter events without corrupting production data.

## Classification

- data_shape_error
- schema_version_mismatch
- terminal_identity_error
- store_scope_error
- duplicate_event
- transient_database_error
- unsupported_event_type

## Procedure

1. Read `/api/v1/sync/dead-letter`.
2. Capture inboxEventId, terminalId, eventType, entityType, attempts, maxAttempts, errorCode, errorMessage, and payload hash.
3. Check whether the event is safe to retry.
4. Retry only when the cause is transient or corrected.
5. Do not retry unknown data-shape events without engineering review.
6. Record replay reason.
7. Confirm event moves to retry_pending and later processed or duplicate.

## Manual retry policy

Manual retry requires reason, owner, timestamp, and post-retry verification. Dead-letter triage is mandatory before broader expansion.
