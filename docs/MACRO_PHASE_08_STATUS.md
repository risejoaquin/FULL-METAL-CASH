# Macro Phase 08 Status - Sales Ticket Ingestion Base

## Goal

Accept a completed POS ticket from a terminal only when the operational context is valid.

Core product rule: no completed sale is accepted without an open cash shift.

## Implemented

- `POST /api/v1/sales`.
- Terminal-aware sale creation from JWT claims.
- Tenant active validation.
- Terminal active validation.
- Open cash shift validation.
- Active cashier validation.
- Optional active customer validation.
- Active product and variant validation.
- Active price validation from PostgreSQL.
- Active payment method validation.
- Idempotency by `(tenant_id, terminal_id, local_sale_id)`.
- Transactional insert into:
  - `sales`
  - `sale_lines`
  - `payments`
- Server-side totals:
  - subtotal
  - discount
  - tax placeholder at `0` for MVP
  - tip
  - total
  - paid
  - change
- Cash payment updates `cash_shifts.expected_cash_cents`.
- Sale response includes persisted lines and payments.
- OpenAPI contract update.
- QSR demo payment methods seed:
  - `cash`
  - `card_manual`
  - `transfer`
- Unit tests for sales contract and service validation.

## Current Limits

- Tax engine is intentionally `0` in this phase; fiscal/tax rules will be a later module.
- Inventory ledger deduction and recipe/BOM consumption are not executed yet; that belongs to the inventory/sync sale side-effects phase.
- Sale void/returns are still pending.

