# SolidPOS V1.1-05 PosCore Error UX & Recovery

## Objective

V1.1-05 improves PosCore operator-facing failure handling and recovery without changing server contracts, schema version 4, sync semantics, sales semantics, inventory semantics, or payment semantics.

## Current Error Flow

- PosCore.Application validates local sale, payment, sync, cash, and hardware operations and throws technical exceptions when an operation is invalid or unavailable.
- PosCore.Infrastructure performs SQLite, HTTP sync, hardware fixture, branding, and update manifest operations.
- PosCore.Wpf previously surfaced simple status strings and did not maintain a reusable operator recovery contract.
- Technical causes remain available through exception type/message and correlation identifiers supplied to the recovery context.

## Operator Recovery Contract

Operator messages are concise, actionable, non-technical, and contain no stack traces, SQL/provider details, secrets, tokens, or connection strings.

Technical diagnostics preserve:

- exception type
- exception message
- operation
- localSaleId when available
- localPaymentId when available
- outboxEventId when available
- deviceType when available
- correlationId when available

## Failure Classification

The PosCore recovery contract distinguishes:

- Retryable
- NonRetryable
- Offline
- Hardware
- Validation
- AuthenticationAuthorization
- Conflict
- Unknown

The operator UI only exposes recovery actions that are valid for the classified failure.

## Safe Retry And Duplicate Protection

Retries must reuse existing local operation identity. A sale retry is safe only when the original localSaleId and outboxEventId are available. A payment retry is safe only when the original localPaymentId and outboxEventId are available.

The V1.1-05 implementation does not create a new sale or payment when an operator presses Retry. It exposes recovery actions and preserves the existing outbox/idempotency boundary.

## Connectivity State

PosCore exposes operator-visible connectivity states:

- ONLINE
- OFFLINE
- RECONNECTING

Offline-first sales remain available while OFFLINE or RECONNECTING.

## Hardware Failures

Receipt printer failures are reported separately from sale failures. If a sale is already saved, the operator sees that the sale was saved and can retry printing.

Cash drawer failures are reported separately from payment failures. If payment is already recorded, the operator sees that the payment was recorded and can retry opening the drawer.

## Recovery Without Restart

Supported recovery actions are:

- Retry
- ContinueOffline
- RetryPrint
- RetryDrawer
- Reconnect
- Dismiss

These actions update PosCore operator state and do not require restarting the application.
