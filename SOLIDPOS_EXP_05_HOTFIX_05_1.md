# SolidPOS EXP-05 HOTFIX 05.1 — Audit Events Occurred At Contract

## Status

PENDING USER VALIDATION

## Failure

EXP-05 passed local guardrails, document contract, secret scan, restore, build, tests, production health/readiness, admin login and monitoring endpoint contract.

It failed only at the SQL operational monitoring cross-check because the query referenced `pos.audit_events.created_at`, which does not exist in the real production schema.

## Root cause

The real audit table contract uses `occurred_at` as the event timestamp. This is also the column used by the audit repository and previous pilot SQL checks.

## Fix

Updated:

```text
scripts/expansion/exp-05-operational-monitoring-hardening-check.sql
```

Changed audit 24-hour counting from:

```sql
ae.created_at >= now() - interval '24 hours'
```

to:

```sql
ae.occurred_at >= now() - interval '24 hours'
```

## Additional audit

The SQL cross-check was reviewed for the same class of prior blockers:

```text
NO pos.inventory_current
NO pos.inventory_ledger.sale_id
NO audit_events.created_at
NO uuid = text comparison without explicit cast in expansion evidence checks
```

Payments remain on `created_at` because `pos.payments.created_at` exists in the PostgreSQL schema and is used by previous pilot monitoring checks.

## Data impact

No backend code, migrations, seeds, dashboard, PosCore, PosBuilder, or production data were changed.

## Expected result

```text
[EXP-05] SQL operational monitoring cross-check PASS
[EXP-05] EXP-05 PASS OPERATIONAL MONITORING HARDENING / GO EXP-06
```
