# LGA-12 Validation Commands

## 1. Prepare secrets

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString
$env:DATABASE_URL = Read-Host "DATABASE_URL"
```

## 2. Unblock validators

```powershell
Unblock-File .\scripts\ga\validate-lga-12-final-limited-ga-closure-or-public-ga-recommendation.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1
```

## 3. Execute LGA-12

```powershell
.\scripts\ga\validate-lga-12-final-limited-ga-closure-or-public-ga-recommendation.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ConflictDecision FORMAL_ARCHIVE `
  -DeadLetterDecision FORMAL_ARCHIVE `
  -CapacityDecision FORMAL_ACCEPT_LIMITED_CAPACITY `
  -FinalDecision CONTINUE_LIMITED_GA `
  -CapacityRecommendation CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA `
  -PublicGaDecision KEEP_LIMITED_GA `
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
  -SkipLga11Revalidation
```

Use `-SkipLga11Revalidation` only because reviewed LGA-11 PASS production logs already exist. Remove it if the local LGA-11 runtime manifest is available and prerequisite enforcement is desired.

## Expected PASS with current capacity state

```text
[LGA-12] PASS LGA-12 FINAL LIMITED GA CLOSURE / CONTINUE LIMITED GA / PUBLIC GA NOT ACTIVATED / CAPACITY UPGRADE REQUIRED BEFORE PUBLIC GA
```

## If it fails
Send the complete PowerShell output beginning at `[LGA-12] Validator version` through the blocker/exception and final manifest values. Do not send passwords, JWTs, `DATABASE_URL`, access tokens or Authorization headers.
