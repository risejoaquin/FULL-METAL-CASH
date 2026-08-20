# SolidPOS PILOT-04 — Receipts / Returns / Refunds Validation

## Status

PENDING USER VALIDATION

## Scope

PILOT-04 validates the production flow for controlled receipts, returns and refunds:

- Controlled sale.
- Protected receipt.
- Digital receipt issue.
- Protected digital receipt lookup.
- Public receipt lookup.
- Email receipt stub.
- Full return.
- Cash refund.
- Inventory restock compensation.
- Cash refund movement.
- Sale status transition to `returned`.
- Shift close with zero difference.
- Audit trail.
- PostgreSQL persistence.
- Pilot log.

## Modules affected

- scripts/pilot
- docs/pilot

No backend, dashboard, database migration, domain model, or production endpoint code was changed.

## Architectural note

PILOT-04 is intentionally script-only because the production backend contract is already deployed. The validator exercises existing production APIs and uses SQL only as an independent persistence check.

## Risk

The main risk is contract drift between script assumptions and production response shape. The script includes defensive handling for envelope shapes and validates final facts through PostgreSQL to reduce false negatives.


## Hotfix 04.1

Se alinea la validación del email receipt stub con el contrato productivo observado: `queued_stub`.
