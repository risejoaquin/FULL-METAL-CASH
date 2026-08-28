# Public GA Stability Burn-In — Validation Commands

```powershell
Unblock-File .\scripts\ga\validate-public-ga-stability-burn-in.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString
$env:DATABASE_URL = Read-Host "DATABASE_URL"

.\scripts\ga\validate-public-ga-stability-burn-in.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SampleCount 3 `
  -SampleIntervalSeconds 30 `
  -MaxStores 2 `
  -AllowedExistingSyncConflictCount 3 `
  -AllowedDeadLetterCount 1 `
  -AllowedNegativeStockItemCount 0 `
  -AllowedOpenShiftCount 0 `
  -AllowedCashDifferenceLast24hCount 0 `
  -AllowedWaitingConnectionCount 12 `
  -MinCompletedSalesInLast24h 6 `
  -MinPaymentsInLast24h 6 `
  -MinReceiptsIssuedInLast24h 3 `
  -MinAuditEventsInLast24h 1 `
  -PublicGaReadinessConcurrency 3 `
  -ConcurrencyProbeRequests 6 `
  -MaxReadinessP95Ms 1200 `
  -WpfVisualConfirmed `
  -SkipDashboardBuild
```

Expected PASS:
`PASS PUBLIC GA STABILITY BURN-IN / PUBLIC GA STABLE / FINAL PRODUCTION CLOSURE AUTHORIZED`
