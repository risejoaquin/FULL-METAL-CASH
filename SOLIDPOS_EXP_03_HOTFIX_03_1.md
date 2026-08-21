# SolidPOS EXP-03 HOTFIX 03.1 — Real Inventory Ledger Reference Contract

Date: 2026-08-20
Status: PENDING USER VALIDATION

## Context

EXP-03 reached production expansion evidence successfully before the SQL cross-check:

- build PASS.
- tests PASS.
- production liveness/readiness PASS.
- admin login and expansion pre-check PASS.
- production data lookup PASS.
- second terminal enrollment/register PASS.
- bootstrap sync and terminal isolation PASS.
- independent cash shift PASS.
- controlled sale from second terminal PASS.
- sale detail, terminal filter and receipt PASS.
- second terminal cash shift close PASS.
- dashboard monitoring and audit evidence PASS.

The failure was isolated to the SQL validator.

## Failure

The SQL validator used a non-existent column:

```sql
pos.inventory_ledger.sale_id
```

Production schema uses the real inventory ledger reference contract already validated in PILOT-02, PILOT-04 and PILOT-05:

```sql
pos.inventory_ledger.reference_type
pos.inventory_ledger.reference_id
```

## Change

Updated:

```text
scripts/expansion/exp-03-second-terminal-expansion-check.sql
```

Before:

```sql
WHERE il.tenant_id = p.tenant_id
  AND il.sale_id = p.sale_id
```

After:

```sql
WHERE il.tenant_id = p.tenant_id
  AND il.reference_type = 'sale'
  AND il.reference_id = p.sale_id
  AND il.quantity_delta < 0
```

## Impact

No backend, PosCore, Dashboard, migrations, seed or production data changes.

This hotfix only aligns the EXP-03 SQL cross-check with the real production inventory ledger contract.

## Expected result

The next validation run should pass:

```text
[EXP-03] SQL second terminal production expansion cross-check PASS
[EXP-03] EXP-03 PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04
```
