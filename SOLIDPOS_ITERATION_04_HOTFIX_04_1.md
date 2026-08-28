# SolidPOS Iteration 04 Hotfix 04.1 — PosCore local runtime validation hardening

## Status

Prepared.

## Problem

The validation command could be executed with placeholder values such as `TERMINAL_ID` and `TERMINAL_TOKEN`.
`TERMINAL_ID` is not a valid GUID, so the PosCore CLI failed while binding the terminal. The PowerShell script kept running because native process exit codes were not explicitly checked.

## Fix

Updated `scripts/poscore/validate-poscore-local-runtime.ps1` to:

- Generate a local validation terminal GUID when `TerminalId` is `TERMINAL_ID`, `AUTO`, or empty.
- Generate a local validation terminal token when `TerminalToken` is `TERMINAL_TOKEN`, `AUTO`, or empty.
- Validate all GUID inputs before invoking the CLI.
- Stop immediately when any `dotnet run` command returns a non-zero exit code.
- Print the generated/used runtime identifiers at the end.

## Correct validation command

```powershell
.\scripts\poscore\validate-poscore-local-runtime.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -TerminalId "AUTO" `
  -TerminalToken "AUTO" `
  -ProductId "dd272b64-d450-4dd5-ace2-b17fc04ecc62"
```

## Expected result

```text
Generated local validation TerminalId: <guid>
Generated local validation TerminalToken.
Initializing local PosCore SQLite runtime...
Binding local terminal...
Creating offline sale and local outbox event...
Checking local outbox...
PosCore local SQLite runtime validation completed.
```

## Architectural note

Iteration 04 validates the local runtime only. It does not require a production terminal token from PosServer yet. Real server-backed terminal enrollment and offline-to-online push will be completed in the next PosCore sync iteration.
