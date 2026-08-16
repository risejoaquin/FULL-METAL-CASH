# Macro Phase 10 Status - Inventory Adjustments Runtime Base

## Goal

Allow manual inventory corrections without updating stock counters directly.

All changes are append-only rows in `inventory_ledger`; current stock remains a read model from `inventory_stock`.

## Implemented

- `POST /api/v1/inventory/adjustments`.
- Protected by `inventory.adjust`.
- Admin/user token flow with explicit `storeId`.
- Terminal-aware service support, although terminal tokens do not include `inventory.adjust` by default.
- Idempotency by `localAdjustmentId` through `inventory_ledger.source_event_id`.
- Validation for:
  - active tenant
  - active store
  - active user
  - active stock-tracked product
  - active unit
  - active variant when provided
  - non-empty reason
  - non-empty lines
  - valid quantity sign
- Adjustment types:
  - `stock_in`
  - `stock_out`
  - `waste`
  - `correction`
- Ledger movement mapping:
  - `waste` -> `waste`
  - all other adjustment types -> `adjustment`
- OpenAPI contract updated.
- Unit tests for adjustment contracts and service validation.

## Quantity Rules

| Adjustment type | Quantity rule |
| --- | --- |
| `stock_in` | Must be positive |
| `stock_out` | Must be negative |
| `waste` | Must be negative |
| `correction` | Can be positive or negative |

## Current Limits

- There is no dedicated `inventory_adjustments` table yet. The adjustment header is reconstructed from grouped ledger metadata.
- Idempotency is application-level through existing ledger rows. A later migration should add a dedicated idempotency table or unique ledger constraint for stronger concurrent protection.
- Purchase orders and supplier receipts remain excluded from MVP scope.

## Smoke Test

Use an owner/admin token:

```powershell
$adjustmentBody = @{
  localAdjustmentId = [guid]::NewGuid()
  storeId = "22222222-2222-2222-2222-222222222222"
  adjustmentType = "stock_in"
  reason = "Correccion manual demo"
  createdByUserId = "33333333-3333-3333-3333-333333333333"
  occurredAt = (Get-Date).ToUniversalTime().ToString("o")
  lines = @(
    @{
      productId = "30000000-0000-0000-0000-000000000004"
      quantityDelta = "18"
      unitId = "11000000-0000-0000-0000-000000000002"
    }
  )
} | ConvertTo-Json -Depth 10

$adjustment = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/inventory/adjustments" `
  -Headers @{ Authorization = "Bearer $($ownerSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $adjustmentBody
```

Then query `GET /api/v1/inventory/stock` again to verify the read model changed.
