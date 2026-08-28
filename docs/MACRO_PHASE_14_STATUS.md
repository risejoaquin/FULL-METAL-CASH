# Macro Phase 14 Status - Sync Push + Process Operational Events

## Goal

Validate that sync does more than acknowledge inbox messages: it must execute real POS operations through the same domain services already used by direct API endpoints.

This phase focuses on operational event payloads sent through `POST /api/v1/sync/push` and executed through `POST /api/v1/sync/process`.

## Implemented

- `sale.voided` can now cancel by `localSaleId`.
- `ISalesService` and `ISalesRepository` support `VoidByLocalSaleIdAsync`.
- Sync processor routes `sale.voided` using:
  - `saleId`, when the server id is known
  - `localSaleId`, when the POS only knows its offline id
- Tests for:
  - sales void by local sale id
  - sync `sale.voided` routing by local sale id

## Operational Event Types To Validate

| Event type | Expected server effect |
| --- | --- |
| `cash.shift.opened` | Creates an open cash shift |
| `sale.completed` | Creates completed sale, payments, sale lines, cash effect and inventory effects |
| `inventory.adjustment.created` | Creates append-only inventory adjustment ledger rows |
| `sale.voided` | Voids sale and compensates cash/inventory |

## Smoke Test Sequence

Use a fresh terminal session. The sequence below creates a batch for cash shift open, then a sale batch, then an inventory adjustment batch, then a sale void batch.

### 1. Push + process cash shift open

```powershell
$openShiftEventId = [guid]::NewGuid()
$openedByUserId = "33333333-3333-3333-3333-333333333333"

$openShiftBody = @{
  batchId = [guid]::NewGuid()
  events = @(
    @{
      eventId = $openShiftEventId
      eventType = "cash.shift.opened"
      entityType = "cash_shift"
      entityId = $terminalRuntime.terminalId
      localOccurredAt = (Get-Date).ToUniversalTime().ToString("o")
      schemaVersion = 1
      payload = @{
        storeId = $null
        terminalId = $null
        openedByUserId = $openedByUserId
        openingAmountCents = 100000
      }
    }
  )
} | ConvertTo-Json -Depth 10

$openShiftPush = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/v1/sync/push" -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } -ContentType "application/json" -Body $openShiftBody

$openShiftProcess = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/v1/sync/process" -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } -ContentType "application/json" -Body (@{ batchId = $openShiftPush.batchId; maxEvents = 100 } | ConvertTo-Json)

$openShiftProcess
```

Expected: `processedCount = 1`.

### 2. Push + process sale completed

```powershell
$localSaleId = [guid]::NewGuid()
$saleEventId = [guid]::NewGuid()

$saleSyncBody = @{
  batchId = [guid]::NewGuid()
  events = @(
    @{
      eventId = $saleEventId
      eventType = "sale.completed"
      entityType = "sale"
      entityId = $localSaleId
      localOccurredAt = (Get-Date).ToUniversalTime().ToString("o")
      schemaVersion = 1
      payload = @{
        localSaleId = $localSaleId
        cashierUserId = $openedByUserId
        customerId = $null
        occurredAt = (Get-Date).ToUniversalTime().ToString("o")
        localCreatedAt = (Get-Date).ToUniversalTime().ToString("o")
        lines = @(
          @{
            productId = "30000000-0000-0000-0000-000000000001"
            variantId = $null
            quantity = "1"
            discountCents = 0
            preparationNote = $null
            modifierIds = @("51000000-0000-0000-0000-000000000001")
          }
        )
        payments = @(
          @{
            localPaymentId = [guid]::NewGuid()
            methodCode = "cash"
            amountCents = 6500
            reference = $null
          }
        )
        tipCents = 0
      }
    }
  )
} | ConvertTo-Json -Depth 10

$saleSyncPush = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/v1/sync/push" -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } -ContentType "application/json" -Body $saleSyncBody

$saleSyncProcess = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/v1/sync/process" -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } -ContentType "application/json" -Body (@{ batchId = $saleSyncPush.batchId; maxEvents = 100 } | ConvertTo-Json)

$saleSyncProcess
```

Expected: `processedCount = 1`.

### 3. Push + process inventory adjustment

```powershell
$adjustmentSyncBody = @{
  batchId = [guid]::NewGuid()
  events = @(
    @{
      eventId = [guid]::NewGuid()
      eventType = "inventory.adjustment.created"
      entityType = "inventory_adjustment"
      entityId = [guid]::NewGuid()
      localOccurredAt = (Get-Date).ToUniversalTime().ToString("o")
      schemaVersion = 1
      payload = @{
        localAdjustmentId = [guid]::NewGuid()
        storeId = $null
        adjustmentType = "stock_in"
        reason = "Reposicion demo via sync"
        createdByUserId = $openedByUserId
        occurredAt = (Get-Date).ToUniversalTime().ToString("o")
        lines = @(
          @{
            productId = "30000000-0000-0000-0000-000000000004"
            variantId = $null
            quantityDelta = "18"
            unitId = "11000000-0000-0000-0000-000000000002"
            costCents = $null
          }
        )
      }
    }
  )
} | ConvertTo-Json -Depth 10

$adjustmentPush = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/v1/sync/push" -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } -ContentType "application/json" -Body $adjustmentSyncBody

$adjustmentProcess = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/v1/sync/process" -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } -ContentType "application/json" -Body (@{ batchId = $adjustmentPush.batchId; maxEvents = 100 } | ConvertTo-Json)

$adjustmentProcess
```

Expected: `processedCount = 1`.

### 4. Push + process sale void by local sale id

```powershell
$voidSyncBody = @{
  batchId = [guid]::NewGuid()
  events = @(
    @{
      eventId = [guid]::NewGuid()
      eventType = "sale.voided"
      entityType = "sale"
      entityId = $localSaleId
      localOccurredAt = (Get-Date).ToUniversalTime().ToString("o")
      schemaVersion = 1
      payload = @{
        saleId = $null
        localSaleId = $localSaleId
        voidedByUserId = $openedByUserId
        reason = "Cancelacion demo via sync"
        occurredAt = (Get-Date).ToUniversalTime().ToString("o")
      }
    }
  )
} | ConvertTo-Json -Depth 10

$voidPush = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/v1/sync/push" -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } -ContentType "application/json" -Body $voidSyncBody

$voidProcess = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/v1/sync/process" -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } -ContentType "application/json" -Body (@{ batchId = $voidPush.batchId; maxEvents = 100 } | ConvertTo-Json)

$voidProcess
```

Expected: `processedCount = 1`.

## DB Verification

```powershell
docker exec -e PGPASSWORD=solidpos_dev_password solidpos-postgres psql -U solidpos -d solidpos -c "SELECT event_type, status, result, error_code, processed_at FROM pos.sync_inbox_events ORDER BY created_at DESC LIMIT 20;"

docker exec -e PGPASSWORD=solidpos_dev_password solidpos-postgres psql -U solidpos -d solidpos -c "SELECT local_sale_id, status, total_cents, paid_cents, change_cents FROM pos.sales ORDER BY created_at DESC LIMIT 10;"

docker exec -e PGPASSWORD=solidpos_dev_password solidpos-postgres psql -U solidpos -d solidpos -c "SELECT product_id, unit_id, quantity_on_hand FROM pos.inventory_stock WHERE store_id = '22222222-2222-2222-2222-222222222222' ORDER BY product_id, unit_id;"
```
