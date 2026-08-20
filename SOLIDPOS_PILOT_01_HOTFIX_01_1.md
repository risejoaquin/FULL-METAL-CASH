# SolidPOS PILOT-01 Hotfix 01.1 — PostgreSQL psql Variable Scope

## Status

PENDING USER VALIDATION

## Issue

The PILOT-01 PostgreSQL validation showed the correct GO row, but failed immediately after it inside the `DO $$ ... $$` block:

```text
ERROR: syntax error at or near ":"
LINE 12: ... WHERE t.id = :'tenant_i...
```

Root cause: `psql` variables such as `:'tenant_id'` are expanded in normal SQL, but not inside the dollar-quoted PL/pgSQL body of `DO $$ ... $$`.

## Fix

`scripts/pilot/pilot-01-store-setup-check.sql` now initializes `pg_temp.pilot_01_state` using psql variables in normal SQL, then the `DO` block reads from that temporary table.

This keeps the same validation semantics:

- active tenant required
- active store required
- active unlocked admin required
- admin store access required
- active sellable product required
- active product price required
- active cash payment method required
- active terminal required

## Files changed

- `scripts/pilot/pilot-01-store-setup-check.sql`

## Validation

Run the same PILOT-01 validation command again.
