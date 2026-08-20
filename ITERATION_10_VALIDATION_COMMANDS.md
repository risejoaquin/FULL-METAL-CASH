# Iteration 10 — Validation Commands

## Restore

```powershell
dotnet restore solidpos-platform.sln
```

## Build

```powershell
dotnet build solidpos-platform.sln
```

## Test

```powershell
dotnet test solidpos-platform.sln
```

## Smoke remoto

```powershell
.\scripts\smoke-test-deployment.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -Email "admin@micafeteria.com" `
  -Password "AdminSeguro123!" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde"
```

## E2E PosCore offline payment/cash drawer

```powershell
.\scripts\poscore\validate-poscore-offline-cash-runtime.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```

## Resultado esperado

```text
Local cash shift opened.
Local cash movement recorded.
Offline cash sale queued from cache.
Remote sync push completed.
Local cash shift closed.
PosCore offline payment/cash drawer runtime completed.
```
