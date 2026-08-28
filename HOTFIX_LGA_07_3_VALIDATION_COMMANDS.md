# HOTFIX LGA-07.3 Validation Commands

## Unblock

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
Unblock-File .\scripts\ga\validate-lga-07-hotfix-07-3-negative-stock-regression-diagnostics-and-correction.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

## Diagnostic only

```powershell
.\scripts\ga\validate-lga-07-hotfix-07-3-negative-stock-regression-diagnostics-and-correction.ps1 `
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

## Apply correction

```powershell
.\scripts\ga\validate-lga-07-hotfix-07-3-negative-stock-regression-diagnostics-and-correction.ps1 `
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

## Then rerun LGA-07

Use the existing LGA-07 validator with `AllowedNegativeStockItemCount 0` and `AllowedWaitingConnectionCount 12`.
