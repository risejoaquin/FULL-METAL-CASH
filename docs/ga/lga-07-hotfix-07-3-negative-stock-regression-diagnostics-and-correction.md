# LGA-07 HOTFIX 07.3 — Negative Stock Regression Diagnostics and Correction

## Purpose

LGA-07.3 diagnoses and corrects the `negative_stock_regression` blocker detected during LGA-07 continued monitoring after fresh real POS activity was generated.

## Scope

This hotfix is diagnostic first. It identifies the affected negative stock item, SKU, store, quantity on hand and controlled adjustment quantity. Correction is applied only when the operator explicitly passes `-ApplyInventoryCorrection`.

## Required contract terms

- lga-07.3
- negative stock regression
- inventory correction
- diagnostic first
- applyinventorycorrection
- public ga not activated
- no baseline increase

## Decisions

- Public GA not activated.
- No baseline increase.
- No acceptance of negative stock.
- Inventory correction must be explicit and auditable.
- LGA-07 remains pending until `negativeStockCount = 0` and the LGA-07 validator passes.

## Expected outcome

Diagnostic-only run may return `CORRECTION REQUIRED BEFORE LGA-07` when negative stock exists.

Correction run should return `PASS HOTFIX LGA-07.3 NEGATIVE STOCK REGRESSION CORRECTION / RERUN LGA-07` when negative stock is back to zero.
