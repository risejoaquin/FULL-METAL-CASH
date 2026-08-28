# SolidPOS Iteration 02 Hotfix 02.2 — E2E Cash Shift Idempotency

## Status
Prepared.

## Problem
The production POS E2E validation script failed with HTTP 409 when opening a cash shift.

Root cause: a previous E2E run opened a shift for the deterministic terminal fingerprint and failed later in the flow, leaving the shift open. The cash shift model correctly enforces one open shift per terminal, so a second run received a conflict.

## Decision
The validation script must be resilient to interrupted E2E runs. Because this is an operations validation script and not product runtime code, it now closes stale open E2E shifts for its deterministic terminal fingerprint before opening a new validation shift.

## Changed file
- `scripts/operations/validate-production-pos-e2e.ps1`

## Changes
- Added `Invoke-DbNonQuery` helper.
- Added `CloseStaleOpenShifts` parameter, default `true`.
- Before opening a new shift, the script closes any open shift owned by the deterministic E2E terminal fingerprint:
  - `iteration-02-e2e-{TenantId}`
- Closed stale shifts use:
  - `status = closed`
  - `counted_cash_cents = expected_cash_cents`
  - `difference_cents = 0`

## Why this is acceptable
This behavior is scoped only to the E2E validation terminal fingerprint. It does not affect normal POS terminals or production users. It prevents operational validation from getting stuck after partial/interrupted runs.

## Validation
Run:

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```

Then re-run:

```powershell
.\scripts\operations\validate-production-pos-e2e.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```

Expected:

```text
Production POS E2E flow completed.
```
