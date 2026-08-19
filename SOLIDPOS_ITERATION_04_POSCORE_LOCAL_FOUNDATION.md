# SolidPOS Iteration 04 — PosCore Local Foundation + SQLite Offline Runtime

## Status

Prepared for validation.

## Objective

Start the real local POS runtime foundation:

- PosCore project structure.
- SQLite local database with WAL enabled.
- Terminal binding stored locally.
- Offline sale draft creation.
- Local outbox event generation.
- Local outbox batch planning.
- First local validation script for offline runtime.

This iteration intentionally does not build the final WPF UI yet. It creates the executable local runtime foundation that the WPF shell will consume.

## Projects added

```text
src/PosCore/SolidPOS.PosCore.Domain
src/PosCore/SolidPOS.PosCore.Application
src/PosCore/SolidPOS.PosCore.Infrastructure
src/PosCore/SolidPOS.PosCore.Cli
tests/SolidPOS.PosCore.UnitTests
```

## Local SQLite tables

```text
terminal_binding
offline_sales
local_outbox_events
```

## Architecture decision

The PosServer cloud contract remains the source of truth. PosCore stores a local SQLite operational cache and a local outbox for offline-first operation. The local runtime does not write directly to PostgreSQL. It queues events locally and later syncs through PosServer sync endpoints.

## Validation

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```

Then run the local PosCore runtime validation with a real tenant/store/terminal/product:

```powershell
.\scripts\poscore\validate-poscore-local-runtime.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -TerminalId "TERMINAL_ID" `
  -TerminalToken "TERMINAL_TOKEN" `
  -ProductId "dd272b64-d450-4dd5-ace2-b17fc04ecc62"
```

Expected:

```text
PosCore local SQLite runtime validation completed.
```

## Completion criteria

- Solution builds.
- Unit tests pass.
- SQLite runtime initializes.
- Terminal binding is persisted locally.
- Offline sale is stored locally.
- Local outbox has a pending `sale.completed` event.
