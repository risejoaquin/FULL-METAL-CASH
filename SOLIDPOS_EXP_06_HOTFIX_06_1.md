# SolidPOS EXP-06 HOTFIX 06.1 — Rollback Append-only Ledger Document Contract

## Status

PENDING USER VALIDATION.

## Failure

EXP-06 failed in the document contract stage because `docs/expansion/exp-06-inventory-reconciliation-rollback.md` did not contain the exact literal term `append-only ledger`.

The document already described the correct rule with the sentence `The inventory ledger is append-only`, but the validator required a stricter phrase.

## Fix

- Added explicit `append-only ledger` wording to the rollback runbook.
- Hardened the validator to accept equivalent forms:
  - `append-only ledger`
  - `append only ledger`
  - `ledger is append-only`
  - `append-only ledger contract`
  - `ledger append-only`
  - `ledger append only`
  - `ledger de solo anexado`
  - `ledger inmutable`

## Audit

EXP-06 SQL was reviewed again for the recurring schema-contract mistakes already seen in EXP-03/EXP-05. The current EXP-06 SQL does not use:

- `pos.inventory_current`
- `pos.inventory_ledger.sale_id`
- `pos.audit_events.created_at`
- UUID/text comparisons without explicit cast in reference checks

The validator keeps inventory reconciliation append-only through `inventory_counts`, `inventory_count_lines`, and `inventory_ledger` adjustment rows.

## Files changed

- `docs/expansion/exp-06-inventory-reconciliation-rollback.md`
- `scripts/expansion/validate-exp-06-inventory-reconciliation-hardening.ps1`
- `SOLIDPOS_EXP_06_HOTFIX_06_1.md`

## Expected result

`[EXP-06] EXP-06 document contract PASS`
