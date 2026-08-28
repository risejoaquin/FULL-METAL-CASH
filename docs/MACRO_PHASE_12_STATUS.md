# Macro Phase 12 Status - Sync Push Base / Outbox Event Ingestion

## Goal

Receive offline-first outbox batches from POS terminals and acknowledge every local event idempotently.

This phase stores events in the server inbox. It does not yet execute event-specific processors for sales, inventory, cash or voids; those domain endpoints already exist and the next sync phases can route inbox events into those workflows.

## Implemented

- `POST /api/v1/sync/push`.
- Protected by `sync.push`.
- Terminal runtime context required:
  - `tenant_id`
  - `store_id`
  - `terminal_id`
- Batch contract:
  - `batchId`
  - `events[]`
- Event contract:
  - `eventId`
  - `eventType`
  - `entityType`
  - `entityId`
  - `localOccurredAt`
  - `schemaVersion`
  - `payload`
- PostgreSQL ingestion into `pos.sync_inbox_events`.
- Database idempotency by `UNIQUE (tenant_id, terminal_id, event_id)`.
- Duplicate event replays return HTTP 200 with `status = duplicate`.
- Duplicate events inside the same payload are rejected per event without inserting twice.
- Runtime migration `005_sync_push_runtime.sql` adds:
  - `batch_id`
  - `schema_version`
  - `sequence_number`
  - batch lookup index
- Logs include batch, tenant, store, terminal and accepted/duplicate/rejected counts.
- Unit tests for contract and service behavior.
- Integration migration helper now applies migration `005`.
- OpenAPI includes the new endpoint through endpoint discovery.

## Rules

| Condition | Result |
| --- | --- |
| Missing terminal runtime claims | Request rejected |
| Empty `batchId` | Request rejected |
| More than 500 events | Request rejected |
| Valid new event | Stored as `received` |
| Replayed event id | Acknowledged as `duplicate` |
| Duplicate event id in same batch | Per-event `rejected` |
| Missing event type/entity/schema/payload | Per-event `rejected` |

## Current Limits

- Events are persisted only; no domain event routing is executed yet.
- Pull/delta sync is still pending.
- Conflict resolution is still pending.
- Event payload schema validation is intentionally light in this phase.

## Smoke Test

Use an active `$terminalSession` and `$terminalRuntime`:

```powershell
$syncEventId = [guid]::NewGuid()

$syncPushBody = @{
  batchId = [guid]::NewGuid()
  events = @(
    @{
      eventId = $syncEventId
      eventType = "pos.health_check"
      entityType = "terminal"
      entityId = $terminalRuntime.terminalId
      localOccurredAt = (Get-Date).ToUniversalTime().ToString("o")
      schemaVersion = 1
      payload = @{
        message = "sync smoke"
        appVersion = "0.1.0-dev"
      }
    }
  )
} | ConvertTo-Json -Depth 10

$syncPush = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/sync/push" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $syncPushBody

$syncPush
```

Expected:

```powershell
$syncPush.acceptedCount
```

Expected value: `1`.

Repeat the same body:

```powershell
$syncPushAgain = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/sync/push" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $syncPushBody

$syncPushAgain.duplicateCount
```

Expected value: `1`.
