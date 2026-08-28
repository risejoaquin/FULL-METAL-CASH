# LGA-02-HOTFIX-03 — Inventory Adjustment Contract Alignment

## Status
READY FOR VALIDATION

## Scope
This hotfix aligns the LGA-02 inventory reconciliation validator with the production inventory adjustment API contract.

## Root Cause
The LGA-02 validator attempted to reconcile ING-CAFE-G negative stock using adjustmentType `stock_count`. The production API accepts `stock_in`, `stock_out`, `waste`, or `correction`, so `stock_count` was rejected with HTTP 409 inventory-adjustment-rejected.

## Fix
The validator now uses adjustmentType `correction` for controlled ING-CAFE-G inventory reconciliation and formats quantityDelta using invariant culture.

## Safety
Public GA remains NOT_ACTIVATED. This hotfix does not modify PosServer runtime behavior, database schema, sync contract, migrations, dashboard, or WPF runtime logic.

## Expected Result
LGA-02 should proceed past the inventory reconciliation gate when the authenticated user has inventory adjustment permissions and the target product/unit/store are valid.
