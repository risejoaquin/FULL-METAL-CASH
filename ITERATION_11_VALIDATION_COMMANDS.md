# Iteration 11 validation commands

## Restore

```powershell
dotnet restore solidpos-platform.sln
```

## Build

```powershell
dotnet build solidpos-platform.sln
```

## Tests

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

## E2E PosCore pull sync + read models

```powershell
.\scripts\poscore\validate-poscore-pull-sync-readmodels.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```

## Resultado esperado

```text
Remote sync pull applied.
Local pull state.
Remote sale read model saved locally.
Remote receipt read model saved locally.
Local read models.
PosCore pull sync local read models completed.
```
