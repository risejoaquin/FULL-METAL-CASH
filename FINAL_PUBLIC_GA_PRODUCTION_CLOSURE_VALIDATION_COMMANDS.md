# Final Public GA Production Closure - Validation Commands

```powershell
Unblock-File .\scripts\ga\validate-final-public-ga-production-closure.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString
$env:DATABASE_URL = Read-Host "DATABASE_URL"

.\scripts\ga\validate-final-public-ga-production-closure.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ClosureDecision CLOSE_SOLIDPOS_V1 `
  -ProductionDecision KEEP_PUBLIC_GA_ACTIVE `
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
  -SkipDashboardBuild `
  -SkipBurnInRevalidation
```

Expected:

`PASS FINAL PUBLIC GA PRODUCTION CLOSURE / SOLIDPOS V1 PRODUCTION BASELINE CLOSED / PUBLIC GA ACTIVE`
