# Iteration 05 Validation Commands

## 1. Restore, build, test

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```

Expected:

```text
0 errors
PosCore.UnitTests PASS
PosServer.UnitTests PASS
IntegrationTests PASS
ContractTests PASS
```

## 2. Validate existing remote API health

```powershell
.\scripts\smoke-test-deployment.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -Email "admin@micafeteria.com" `
  -Password "AdminSeguro123!" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde"
```

## 3. Validate PosCore local runtime

```powershell
.\scripts\poscore\validate-poscore-local-runtime.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -TerminalId "AUTO" `
  -TerminalToken "AUTO" `
  -ProductId "dd272b64-d450-4dd5-ace2-b17fc04ecc62"
```

## 4. Validate offline-to-online sync

```powershell
.\scripts\poscore\validate-poscore-offline-to-online-sync.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!" `
  -ProductId "dd272b64-d450-4dd5-ace2-b17fc04ecc62"
```

Expected:

```text
Remote sync push completed.
PosCore offline-to-online sync completed.
```
