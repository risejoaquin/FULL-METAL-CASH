# SolidPOS PILOT-02 Hotfix 02.2 — PostgreSQL Payments Table Contract

## Status

Prepared for user validation.

## Problem

The PILOT-02 SQL validation referenced `pos.sale_payments`, but the production PostgreSQL schema and PosServer repository use `pos.payments`.

Production execution had already passed the API-level sale, payment, receipt, shift, audit and read-model validations, then failed only at the final SQL persistence validation.

## Fix

Updated:

- `scripts/pilot/pilot-02-transaction-check.sql`
- `SOLIDPOS_PILOT_02_REAL_POS_TRANSACTION_VALIDATION.md`

Changes:

- `pos.sale_payments` -> `pos.payments`
- payment status assertion `captured` -> `approved`, matching the production schema.

## Architectural Decision

The canonical production payment persistence table is `pos.payments`. PILOT-02 must validate against the real schema instead of a derived/read-model name.

## Expected Result

The PILOT-02 validator should pass the final PostgreSQL persistence check and complete with:

```text
[PILOT-02] Validating transaction persistence via PostgreSQL PASS
goNoGo : GO
message : SolidPOS PILOT-02 real POS transaction validation completed.
```
