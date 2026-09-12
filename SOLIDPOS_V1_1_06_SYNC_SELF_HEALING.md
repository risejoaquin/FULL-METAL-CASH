# SolidPOS V1.1-06 Sync Self-Healing

## Sync Self-Healing Contract

V1.1-06 adds bounded local recovery behavior for PosCore sync outbox processing without changing syncContract schema_version_4.

Required behavior:

- Pending and retry_pending sync events must converge to zero when failures are recoverable.
- Recoverable failures move events to retry_pending with bounded retry attempts.
- Retry delay must use increasing backoff plus jitter to avoid tight loops.
- Repeated or non-recoverable poison events must become explicit dead_letter events.
- Dead-lettered events must remain visible for diagnostics and must not disappear during recovery.
- Retry and recovery must preserve original event identity, tenant, store, terminal, sequence number, payload, and schemaVersion 4.
- Duplicate remote acknowledgements are idempotent and must not create duplicate business side effects.
- Queue health summary must include pending, processing, retry_pending, dead_letter, oldest pending age, oldest retry age, stuck processing, and recovery indicators.

## Preserved Guarantees

- schemaVersion: 4
- syncContract schema_version_4
- Tenant isolation is preserved by rejecting mixed tenant/store/terminal batches.
- Existing conflict semantics remain unchanged.
- Last-Write-Wins is not introduced.
- Retries do not generate duplicate business operations.

## Failure Classification

Recoverable failures include transient network, timeout, cancellation, and temporary remote availability failures.

Non-recoverable failures include malformed payloads, invalid local contracts, and events that exceed the maximum retry attempts.

## Operational Diagnostics

Operators can inspect queue health using the local queue health summary model:

- PendingCount
- ProcessingCount
- RetryPendingCount
- DeadLetterCount
- OldestPendingAtUtc
- OldestRetryPendingAtUtc
- HasStuckProcessing
- RequiresRecovery
