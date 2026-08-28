# Public GA Readiness Review — Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos-public-ga-review

Unblock-File .\scripts\ga\validate-public-ga-readiness-review.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString
$env:DATABASE_URL = Read-Host "DATABASE_URL"

.\scripts\ga\validate-public-ga-readiness-review.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ConflictDecision FORMAL_ARCHIVE `
  -DeadLetterDecision FORMAL_ARCHIVE `
  -CapacityDecision CAPACITY_GATE_PASSED `
  -ReadinessDecision RECOMMEND_PUBLIC_GA_GO `
  -ActivationDecision KEEP_NOT_ACTIVATED `
  -MaxStores 2 `
  -MaxConcurrentTerminals 2 `
  -MinCompletedSalesInLast24h 6 `
  -MinPaymentsInLast24h 6 `
  -MinReceiptsIssuedInLast24h 3 `
  -MinAuditEventsInLast24h 1 `
  -MinClosedShiftsTotal 1 `
  -AllowedCashDifferenceLast24hCount 0 `
  -AllowedExistingSyncConflictCount 3 `
  -AllowedDeadLetterCount 1 `
  -AllowedNegativeStockItemCount 0 `
  -AllowedOpenShiftCount 0 `
  -AllowedWaitingConnectionCount 12 `
  -PublicGaReadinessConcurrency 3 `
  -ConcurrencyProbeRequests 6 `
  -MaxReadinessP95Ms 1200 `
  -WpfVisualConfirmed `
  -SkipDashboardBuild `
  -SkipPostLgaCapacityRevalidation
```

Expected PASS:

`PASS PUBLIC GA READINESS REVIEW / GO RECOMMENDATION AUTHORIZED / PUBLIC GA NOT ACTIVATED`
