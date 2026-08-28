# EXP-07 — Sync Replay Policy

## Replay safety

Replay is allowed only for events with a known transient failure or after the underlying cause has been corrected.

## Required fields

- inbox event id
- event id
- terminal id
- store id
- reason
- actor
- timestamp
- previous status
- target status

## Forbidden replay

Do not replay events when store scope is unknown, terminal identity is invalid, payload shape is unsupported, or the event could double-charge/double-count without idempotency proof.
