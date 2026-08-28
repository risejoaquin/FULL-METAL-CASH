# LGA-03 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-lga-03-limited-ga-multi-day-stability-burn-in.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

Select-String .\scripts\ga\validate-lga-03-limited-ga-multi-day-stability-burn-in.ps1 -Pattern "LGA-03.2-dashboard-overview-endpoint-contract-alignment"

if ([string]::IsNullOrWhiteSpace($env:DATABASE_URL)) {
  throw "DATABASE_URL no está configurado en esta sesión."
} else {
  "DATABASE_URL presente en sesión: OK"
}

.\scripts\ga\validate-lga-03-limited-ga-multi-day-stability-burn-in.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ConflictDecision FORMAL_ARCHIVE `
  -DeadLetterDecision FORMAL_ARCHIVE `
  -BurnInCheckpoint 1 `
  -RequiredBurnInDays 3 `
  -MaxStores 2 `
  -MaxConcurrentTerminals 2 `
  -MinCompletedSalesInLast24h 6 `
  -MinPaymentsInLast24h 6 `
  -MinReceiptsIssuedInLast24h 3 `
  -AllowedExistingSyncConflictCount 3 `
  -AllowedDeadLetterCount 1 `
  -AllowedNegativeStockItemCount 0 `
  -AllowedOpenShiftCount 0 `
  -AllowedWaitingConnectionCount 11 `
  -WpfVisualConfirmed `
  -SkipDashboardBuild `
  -SkipLga02Revalidation
```

## Day 2

Cambiar `-BurnInCheckpoint 2`.

## Day 3 / finalización

Cambiar `-BurnInCheckpoint 3` y agregar `-FinalizeBurnIn`.


## LGA-03-HOTFIX-01 — Sales Range Endpoint Contract Alignment

Validator aligned with production sales range endpoint `/api/v1/reports/sales/range`. Public GA remains NOT_ACTIVATED.
