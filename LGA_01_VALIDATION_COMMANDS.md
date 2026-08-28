# LGA-01 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-lga-01-limited-ga-operations-hardening.ps1
Unblock-File .\scripts\ga\validate-cga-04-public-ga-activation-decision.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

Select-String .\scripts\ga\validate-lga-01-limited-ga-operations-hardening.ps1 -Pattern "LGA-01.0-limited-ga-operations-hardening"

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString

if ([string]::IsNullOrWhiteSpace($env:DATABASE_URL)) {
  throw "DATABASE_URL no está configurado en esta sesión."
} else {
  "DATABASE_URL presente en sesión: OK"
}

.\scripts\ga\validate-lga-01-limited-ga-operations-hardening.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ConflictDecision FORMAL_ARCHIVE `
  -DeadLetterDecision FORMAL_ARCHIVE `
  -InventoryDecision OBSERVE `
  -MaxStores 2 `
  -MaxConcurrentTerminals 2 `
  -AllowedExistingSyncConflictCount 3 `
  -AllowedDeadLetterCount 1 `
  -AllowedNegativeStockItemCount 1 `
  -AllowedWaitingConnectionCount 11 `
  -SkipDashboardBuild `
  -SkipCga04Revalidation
```

Optional inventory adjustment mode:

```powershell
  -InventoryDecision ADJUSTED `
  -ApplyInventoryAdjustment
```
