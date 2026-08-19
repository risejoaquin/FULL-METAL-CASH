# Iteration 09 — Validation Commands

## 1. Build/tests

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```

## 2. Smoke remoto

```powershell
.\scripts\smoke-test-deployment.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -Email "admin@micafeteria.com" `
  -Password "AdminSeguro123!" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde"
```

## 3. Cargar DATABASE_URL para reconciliación de inventario remoto

```powershell
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Length
$env:DATABASE_URL.Substring(0,13)
```

Debe iniciar con:

```text
postgresql://
```

## 4. E2E Iteration 09

```powershell
.\scripts\poscore\validate-poscore-local-inventory-consumption-cache.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```

## Resultado esperado

```text
Local inventory cache refreshed.
Offline sale queued from cache with inventory.
Remote sync push completed.
Inventory reconciliation matched.
PosCore local inventory consumption cache completed.
```
