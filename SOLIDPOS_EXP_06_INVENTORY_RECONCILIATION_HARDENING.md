# SolidPOS EXP-06 — Inventory Reconciliation Hardening

## Status

PENDING USER VALIDATION.

## Objective

Close the operational inventory condition reported by EXP-05:

- `negative_inventory_requires_reconciliation`

EXP-06 hardens inventory reconciliation and prepares SolidPOS for EXP-07 Sync SLA and Offline Reliability Hardening.

## Scope

- Derived stock calculation from append-only `pos.inventory_ledger`.
- Negative inventory diagnosis.
- Controlled inventory count and adjustment evidence.
- Modifier semantics validation.
- Substitute modifier validation using `replaces_product_id`.
- Recipe/BOM validation.
- Low stock threshold coverage review.
- GO/NO-GO decision for EXP-07.

## Production write behavior

If negative inventory exists, the validator creates controlled append-only reconciliation evidence:

- `pos.inventory_counts`
- `pos.inventory_count_lines`
- `pos.inventory_ledger`

It does not update or delete existing ledger rows.

## Files added

- `scripts/expansion/validate-exp-06-inventory-reconciliation-hardening.ps1`
- `scripts/expansion/exp-06-inventory-reconciliation-hardening-check.sql`
- `docs/expansion/exp-06-inventory-reconciliation-hardening.md`
- `docs/expansion/exp-06-inventory-diagnostic-report.md`
- `docs/expansion/exp-06-reconciliation-runbook.md`
- `docs/expansion/exp-06-modifier-recipe-rules.md`
- `docs/expansion/exp-06-inventory-alerts-thresholds.md`
- `docs/expansion/exp-06-inventory-reconciliation-rollback.md`
- `docs/expansion/exp-06-go-no-go.md`
- `EXP_06_VALIDATION_COMMANDS.md`

## Expected result

`EXP-06 PASS INVENTORY RECONCILIATION HARDENING / GO EXP-07`


## HOTFIX 06.1

Rollback document contract hardened to include explicit append-only ledger wording and equivalent validator terms.
