# SolidPOS Iteration 05 — PosCore Offline-to-Online Sync Runtime

## Objective

Close the first real runtime bridge between local PosCore SQLite and remote PosServer sync endpoints.

This iteration starts from the local PosCore runtime created in Iteration 04 and adds the ability to:

- Read pending local outbox events.
- Build a server-compatible `/api/v1/sync/push` payload.
- Push the batch to the remote PosServer using a terminal access token.
- Treat accepted and duplicate events as acknowledged.
- Mark local outbox events as synced.
- Persist remote acknowledgement metadata locally.
- Run an E2E offline-local to online-remote validation against Railway/Supabase.

## Scope

Included:

- PosCore remote sync push service.
- HTTP client for PosServer `/api/v1/sync/push`.
- SQLite local acknowledgement table.
- CLI command `sync-push`.
- Operational E2E script.
- Unit tests for sync push behavior.

Not included yet:

- Full WPF UI.
- Continuous background sync worker.
- Advanced retry scheduler.
- Local conflict-resolution UX.
- Full catalog bootstrap into SQLite.

Those belong in the next iterations.

## Local SQLite changes

The local runtime now creates:

```text
terminal_binding
offline_sales
local_outbox_events
local_sync_acknowledgements
```

`local_sync_acknowledgements` stores remote acknowledgement snapshots from PosServer.

## New CLI command

```powershell
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- `
  sync-push `
  --db .\.runtime\poscore-iteration-05.sqlite `
  --base-url https://full-metal-cash-production.up.railway.app `
  --batch-id <batch-guid> `
  --limit 500
```

The command uses the locally stored terminal token from `terminal_binding` unless `--terminal-access-token` is provided.

## Operational validation

Use:

```powershell
.\scripts\poscore\validate-poscore-offline-to-online-sync.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!" `
  -ProductId "dd272b64-d450-4dd5-ace2-b17fc04ecc62"
```

Expected result:

```text
PosCore offline-to-online sync completed.
```

## Architectural decision

PosCore owns the local outbox. PosServer owns the remote inbox, processing, conflict state and pull stream.

This keeps the offline-first boundary clean:

```text
PosCore SQLite local_outbox_events -> PosServer /sync/push -> PosServer sync_inbox_events
```

The UI will later call PosCore application services, not PosServer directly for offline sales.
