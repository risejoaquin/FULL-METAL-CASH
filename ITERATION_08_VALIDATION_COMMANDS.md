# SolidPOS Iteration 08 — Validation Commands

## 1. Restore

```powershell
dotnet restore solidpos-platform.sln
```

## 2. Build

```powershell
dotnet build solidpos-platform.sln
```

Esperado:

```text
Compilación correcta.
0 Advertencia(s)
0 Errores
```

## 3. Tests

```powershell
dotnet test solidpos-platform.sln
```

Esperado mínimo:

```text
SolidPOS.PosCore.UnitTests PASS
SolidPOS.PosServer.UnitTests PASS
SolidPOS.PosServer.IntegrationTests PASS
SolidPOS.PosServer.ContractTests PASS
```

## 4. Smoke remoto

```powershell
.\scripts\smoke-test-deployment.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -Email "admin@micafeteria.com" `
  -Password "AdminSeguro123!" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde"
```

## 5. E2E local catalog cache → offline sale → sync remoto

```powershell
.\scripts\poscore\validate-poscore-local-catalog-inventory-cache.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```

Esperado:

```text
Local catalog cache refreshed.
Catalog product cached.
Offline sale queued from cache.
Remote sync push completed.
PosCore local catalog cache offline sale completed.
```
