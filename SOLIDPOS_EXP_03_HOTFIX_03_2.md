# SolidPOS EXP-03 HOTFIX 03.2 — Audit Entity UUID/Text Cast Contract

## Status

PENDING USER VALIDATION

## Failure

EXP-03 passed the production expansion workflow through terminal enrollment/register, bootstrap sync, independent cash shift, controlled sale, receipt validation, shift close, dashboard monitoring, and audit evidence.

The remaining failure was isolated to the SQL cross-check:

```text
operator does not exist: uuid = text
```

## Root cause

`pos.audit_events.entity_id` is typed as UUID in the production schema while the SQL cross-check compared it against `p.sale_id::text`.

## Fix

The SQL cross-check now normalizes both sides to text when comparing flexible reference identifiers:

```sql
il.reference_id::text = p.sale_id::text
ae.entity_id::text = p.sale_id::text
```

## Files changed

```text
scripts/expansion/exp-03-second-terminal-expansion-check.sql
SOLIDPOS_EXP_03_HOTFIX_03_2.md
```

## No production data changed

This hotfix only changes validation SQL and documentation. It does not touch backend code, migrations, seeds, or production data.

## Expected result

```text
[EXP-03] SQL second terminal production expansion cross-check PASS
[EXP-03] EXP-03 PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04
```
