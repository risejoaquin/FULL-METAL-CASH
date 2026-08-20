# SolidPOS PILOT-03 — Cash Drawer / Shift Operations Validation

## Status

PENDING USER VALIDATION

## Objective

Validate the production cash drawer lifecycle beyond a single sale:

- cash shift opening;
- current open shift lookup;
- controlled cash in;
- controlled cash out;
- drawer open without sale;
- two controlled cash sales accumulated in the same shift;
- shift summary consistency;
- shift closing with zero difference;
- audit trail for shift and movements;
- PostgreSQL persistence validation;
- operational pilot log.

## Architectural decision

PILOT-03 validates the existing production API contract instead of bypassing application services with direct SQL writes.

Direct SQL is only used for:

- production lookup of known pilot data;
- stale PILOT-03 open shift cleanup;
- final persistence assertion.

All operational mutations are executed through production endpoints:

- `POST /api/v1/cash-drawers/shifts`
- `GET /api/v1/cash-drawers/shifts/current`
- `POST /api/v1/cash-drawers/shifts/{shiftId}/movements`
- `GET /api/v1/cash-drawers/shifts/{shiftId}/summary`
- `POST /api/v1/cash-drawers/shifts/{shiftId}/close`
- `POST /api/v1/sales`
- `GET /api/v1/audit/events`

## Expected arithmetic

Default values:

```text
OpeningAmountCents 30000
CashInCents         5000
CashOutCents        2000
Sale1Cents          4500
Sale2Cents          4500
CashSalesCents      9000
ExpectedCashCents   42000
CountedCashCents    42000
DifferenceCents     0
```

Formula:

```text
expected cash = opening + cash in - cash out + cash sales
```

## Production tables validated

- `pos.cash_shifts`
- `pos.cash_movements`
- `pos.sales`
- `pos.payments`
- `pos.audit_events`

## Risk controls

- Uses a dedicated PILOT-03 terminal fingerprint.
- Closes stale open PILOT-03 shifts before starting.
- Uses exact tender to avoid ambiguity with change handling.
- Requires zero cash difference on close.
- Requires audit events for open, close and all cash movements.
- Requires SQL assertion to return `PASS` and `GO`.

## GO criteria

PILOT-03 is GO only if:

- one cash shift opens;
- current open shift matches the newly opened shift;
- cash in, cash out and drawer open movements are recorded;
- two controlled cash sales are accumulated in the same shift;
- expected cash matches the formula;
- shift closes with zero difference;
- audit trail exists;
- PostgreSQL persistence assertion returns GO;
- `docs/pilot/logs/pilot-03-cash-shift-log.md` is created.
