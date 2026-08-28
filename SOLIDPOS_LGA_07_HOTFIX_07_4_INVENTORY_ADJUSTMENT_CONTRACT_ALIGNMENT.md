# SolidPOS LGA-07 HOTFIX 07.4 — Inventory Adjustment Contract Alignment

## Status

Prepared after LGA-07.3 correction failed with HTTP 409 from `/api/v1/inventory/adjustments`.

## Decision

Use `AdjustmentType = correction` for manual negative stock remediation. Do not use `stock_count`, because the real inventory adjustment endpoint validates allowed adjustment types independently from inventory count reconciliation.

## Affected files

- `scripts/ga/validate-lga-07-hotfix-07-4-inventory-adjustment-contract-alignment.ps1`
- `scripts/ga/lga-07-hotfix-07-4-inventory-adjustment-contract-alignment-check.sql`
- `docs/ga/lga-07-hotfix-07-4-inventory-adjustment-contract-alignment.md`
- `HOTFIX_LGA_07_4_VALIDATION_COMMANDS.md`

## Guardrails

- No Public GA activation.
- No acceptance of negative inventory.
- No waiting connection baseline increase.
- No DB mutation unless `-ApplyInventoryCorrection` is explicitly passed.
