# LGA-10 Validation Commands

## 1. Prepare secrets

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString
$env:DATABASE_URL = Read-Host "DATABASE_URL"
```

## 2. Unblock validators

```powershell
Unblock-File .\scripts\ga\validate-lga-10-limited-ga-commercial-operations-confidence-gate.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1
```

## 3. Execute LGA-10 — Limited GA commercial operations confidence gate

```powershell
.\scripts\ga\validate-lga-10-limited-ga-commercial-operations-confidence-gate.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ConflictDecision FORMAL_ARCHIVE `
  -DeadLetterDecision FORMAL_ARCHIVE `
  -CapacityDecision FORMAL_ACCEPT_LIMITED_CAPACITY `
  -CommercialDecision CONTINUE_LIMITED_GA `
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
  -SkipLga09Revalidation
```

Use `-SkipLga09Revalidation` only because LGA-09 already has reviewed PASS production logs. Remove it if the LGA-09 runtime manifest exists locally and you want prerequisite manifest enforcement.

## Expected PASS

```text
[LGA-10] PASS LGA-10 LIMITED GA COMMERCIAL OPERATIONS CONFIDENCE GATE / CONTINUE LIMITED GA
```

## If it fails

Send the complete PowerShell output beginning at `[LGA-10] Validator version` through the blocker/exception and final manifest values. Do not send passwords, JWTs, `DATABASE_URL`, access tokens or Authorization headers.
