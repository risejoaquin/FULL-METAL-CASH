# Macro Phase 15 Status - Sync Pull / Delta Download Base

## Goal

Allow a POS terminal to download cloud-side runtime data and deltas using a cursor.

This is the first pull side of offline-first sync. Push/process sends local events to the server; pull downloads server state back to the local POS.

## Implemented

- `GET /api/v1/sync/pull`.
- Protected by `sync.pull`.
- Terminal runtime context required:
  - `tenant_id`
  - `store_id`
  - `terminal_id`
- Query parameters:
  - `cursor`
  - `limit`
- Response includes:
  - `serverTime`
  - `previousCursor`
  - `nextCursor`
  - `hasMore`
  - terminal runtime context
  - changes array
- Bootstrap pull when `cursor` is empty:
  - `tenant.config`
  - `tenant.catalog`
  - `tenant.access`
  - `terminal.runtime`
- Delta pull from `pos.sync_changes` when `cursor` is present.
- Store-aware filtering:
  - global changes where `store_id IS NULL`
  - store-specific changes for the terminal store
- Self-echo filtering:
  - excludes changes where `source_terminal_id` equals the current terminal
- Unit tests for:
  - pull contract
  - missing terminal context
  - invalid cursor
  - bootstrap snapshots + deltas

## Bootstrap Change Types

| Entity type | Purpose |
| --- | --- |
| `tenant.config` | POS Builder runtime config |
| `tenant.catalog` | Categories, products, variants, barcodes, prices, modifiers and BOM |
| `tenant.access` | Users, roles and permissions snapshot |
| `terminal.runtime` | Tenant/store/terminal binding |

## Delta Source

Future server-side changes are read from `pos.sync_changes`:

| Column | Meaning |
| --- | --- |
| `entity_type` | Changed resource type |
| `entity_id` | Changed resource id |
| `operation` | `create`, `update`, `delete` |
| `entity_version` | Version for optimistic sync |
| `changed_at` | Cursor boundary |
| `payload` | JSONB payload |
| `store_id` | Optional store scope |
| `source_terminal_id` | Optional source terminal for echo filtering |

## Current Limits

- Catalog/config updates do not yet automatically write rows into `sync_changes`.
- Cursor is currently timestamp-based using `nextCursor = serverTime`.
- No compacted cursor with `(changed_at, id)` yet.
- No pull compression/pagination continuation token beyond `hasMore`.

## Smoke Test

Use an active `$terminalSession`:

```powershell
$pull = Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:5000/api/v1/sync/pull?limit=100" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" }

$pull.changes | Select-Object entityType,operation,entityVersion
$pull.nextCursor
```

Expected bootstrap entities:

```text
tenant.config
tenant.catalog
tenant.access
terminal.runtime
```

Then call with cursor:

```powershell
$cursor = [uri]::EscapeDataString($pull.nextCursor)

$delta = Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:5000/api/v1/sync/pull?cursor=$cursor&limit=100" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" }

$delta.changes.Count
$delta.previousCursor
$delta.nextCursor
```

Expected in the current demo DB: `0` or only rows explicitly inserted into `pos.sync_changes`.

## DB Verification

```powershell
docker exec -e PGPASSWORD=solidpos_dev_password solidpos-postgres psql -U solidpos -d solidpos -c "SELECT entity_type, operation, entity_version, changed_at, store_id, source_terminal_id FROM pos.sync_changes ORDER BY changed_at DESC LIMIT 20;"
```
