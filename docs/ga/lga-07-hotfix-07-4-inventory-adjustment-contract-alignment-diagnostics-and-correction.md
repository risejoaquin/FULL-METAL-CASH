# LGA-07.4 — Inventory Adjustment Contract Alignment

## Purpose

Align the LGA-07 negative stock correction with the real `/api/v1/inventory/adjustments` contract.

## Root cause

LGA-07.3 correctly diagnosed the negative stock regression but used `adjustmentType = stock_count` for the correction payload. The API contract only accepts adjustment types validated by the inventory adjustment service. `stock_count` belongs to inventory count reconciliation, not manual inventory adjustment.

## Contract alignment

The correction adjustment type is `correction`.

The correction request uses:

- `LocalAdjustmentId`
- `StoreId`
- `AdjustmentType = correction`
- `Reason`
- `CreatedByUserId`
- `OccurredAt`
- `Lines`
  - `ProductId`
  - `VariantId`
  - `QuantityDelta`
  - `UnitId`
  - `CostCents`

## Safety guarantees

- Public GA not activated.
- No baseline increase.
- Negative stock is not accepted.
- The correction is only applied when `-ApplyInventoryCorrection` is explicitly supplied.
- The first run remains diagnostic only.

## Expected outcome

After correction, `negativeStockCount` must be `0`, then LGA-07 must be rerun with `AllowedNegativeStockItemCount 0`.
