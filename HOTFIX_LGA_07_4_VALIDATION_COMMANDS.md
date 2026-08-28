# HOTFIX LGA-07.4 Validation Commands

## 1. Unblock

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-lga-07-hotfix-07-4-inventory-adjustment-contract-alignment.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

## 2. Diagnostic run

```powershell
.\scripts\ga\validate-lga-07-hotfix-07-4-inventory-adjustment-contract-alignment.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -AllowedNegativeStockItemCount 0 `
  -AllowedWaitingConnectionCount 12 `
  -SkipDashboardBuild
```

## 3. Correction run

```powershell
.\scripts\ga\validate-lga-07-hotfix-07-4-inventory-adjustment-contract-alignment.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -AllowedNegativeStockItemCount 0 `
  -AllowedWaitingConnectionCount 12 `
  -ApplyInventoryCorrection `
  -SkipDashboardBuild
```

## 4. Rerun LGA-07

Rerun the normal LGA-07 validator after the correction reports `negativeStockCount = 0`.
