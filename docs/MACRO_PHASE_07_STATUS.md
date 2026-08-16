# Macro Phase 07 Status - Cash Shifts Runtime Base

## Goal

Implement the cash shift runtime base required before sales ingestion.

Core product rule: no sale can be accepted unless the terminal has an open cash shift.

## Implemented

- `GET /api/v1/cash-drawers/shifts/current`.
- `POST /api/v1/cash-drawers/shifts`.
- `POST /api/v1/cash-drawers/shifts/{shiftId}/movements`.
- `POST /api/v1/cash-drawers/shifts/{shiftId}/close`.
- Tenant/store/terminal-aware runtime from authenticated claims.
- One open shift per tenant/terminal enforced by PostgreSQL unique partial index.
- Transactional cash movements with `FOR UPDATE` shift locking.
- Expected cash tracking:
  - Opening amount initializes expected cash.
  - `cash_in` increases expected cash.
  - `cash_out` decreases expected cash.
  - `drawer_open_no_sale` audits drawer opens without changing expected cash.
- Close shift calculates `difference_cents = counted_cash_cents - expected_cash_cents`.
- RBAC policies:
  - Current/open: `cash.open`
  - Movements: `cash.move`
  - Close: `cash.close`
- OpenAPI contract update.
- Unit test for cash shift contract shape.

## Runtime Contract

Opening a shift requires an active tenant, active terminal, active store relation, and active user.

Terminal tokens may omit `storeId` and `terminalId` in the body because those values are derived from claims.

## Current Limit

This phase does not ingest sales yet. Macro Phase 08 must call `GetCurrentOpenShiftAsync` before creating any sale.

