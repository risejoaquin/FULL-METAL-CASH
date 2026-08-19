# Iteration 07 — Validation Commands

## 1. Build/test

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

## 3. E2E semantic offline sale

```powershell
.\scripts\poscore\validate-poscore-offline-sale-semantic-processing.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!" `
  -ProductId "dd272b64-d450-4dd5-ace2-b17fc04ecc62"
```

## 4. Expected E2E output

```text
PosCore offline sale semantic processing completed.
processedCount >= 1
saleTotalCents = 4500
cashSalesCents = 4500
differenceCents = 0
syncStatusDeadLetterCount = 0
```
