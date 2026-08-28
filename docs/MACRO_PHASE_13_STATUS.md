# Macro Phase 13 Status - Sync Event Processing Base

## Goal

Move inbox events from `received` into executable server workflows without losing idempotency or tenant isolation.

This phase introduces the first explicit event processor for POS offline-first sync. It claims pending events, routes supported `eventType` values into existing domain services, and records the final result on `sync_inbox_events`.

## Implemented

- `POST /api/v1/sync/process`.
- Protected by `sync.push`.
- Terminal runtime context required:
  - `tenant_id`
  - `store_id`
  - `terminal_id`
- Processor request:
  - `batchId`
  - `maxEvents`
- Processor response:
  - `receivedCount`
  - `processedCount`
  - `rejectedCount`
  - per-event result rows
- Atomic event claiming:
  - `received` -> `processing`
  - uses `FOR UPDATE SKIP LOCKED`
- Final event states:
  - `processed`
  - `rejected`
- PostgreSQL migration `006_sync_processing_runtime.sql`:
  - expands inbox status check with `processing`
  - adds processing lookup index
- Logs include tenant/store/terminal/batch and processed/rejected totals.
- Unit tests for:
  - missing terminal context
  - health check no-op processing
  - `sale.completed` routing into sales service
  - unsupported event rejection
- Integration migration helper applies migration `006`.

## Supported Event Types

| Event type | Processor behavior |
| --- | --- |
| `pos.health_check` | Marks event processed without domain mutation |
| `sale.completed` | Deserializes payload as `CreateSaleRequest` and calls sales service |
| `sale.voided` | Deserializes payload with `saleId` or `localSaleId` + void request fields and calls sale void |
| `inventory.adjustment.created` | Deserializes payload as `CreateInventoryAdjustmentRequest` |
| `cash.shift.opened` | Deserializes payload as `OpenCashShiftRequest` |
| `cash.movement.created` | Deserializes payload with `cashShiftId` + movement fields |
| `cash.shift.closed` | Deserializes payload with `cashShiftId` + close fields |

## Rules

| Condition | Result |
| --- | --- |
| Event status is `received` | Can be claimed |
| Event is claimed | Status becomes `processing` |
| Domain service returns success | Status becomes `processed` |
| Domain service returns null | Status becomes `rejected` with domain error code |
| Unsupported event type | Status becomes `rejected` with `unsupported_event_type` |
| Event already processed/rejected | Not picked again |

## Current Limits

- No background worker yet; processing is manual through `POST /sync/process`.
- Events stuck in `processing` after a process crash require a recovery policy in a later phase.
- Event schema validation is still contract-level/deserialization-level; full event schema registry is pending.
- Pull/delta sync is still pending.

## Smoke Test

After a successful sync push:

```powershell
$processBody = @{
  batchId = $syncPush.batchId
  maxEvents = 100
} | ConvertTo-Json

$syncProcess = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/sync/process" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $processBody

$syncProcess
```

Expected for the current `pos.health_check` smoke event:

```powershell
$syncProcess.processedCount
```

Expected value: `1`.

DB verification:

```powershell
docker exec -e PGPASSWORD=solidpos_dev_password solidpos-postgres psql -U solidpos -d solidpos -c "SELECT batch_id, event_id, event_type, status, result, error_code, processed_at FROM pos.sync_inbox_events ORDER BY created_at DESC LIMIT 10;"
```
