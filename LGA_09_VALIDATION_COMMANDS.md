# LGA-09 Validation Commands

## 1. Prepare secrets

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString
$env:DATABASE_URL = Read-Host "DATABASE_URL"
```

## 2. Unblock validators

```powershell
Unblock-File .\scripts\ga\validate-lga-09-limited-ga-stability-confirmation-capacity-risk-review.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1
```

## 3. Execute LGA-09 — current Limited GA path

```powershell
.\scripts\ga\validate-lga-09-limited-ga-stability-confirmation-capacity-risk-review.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ConflictDecision FORMAL_ARCHIVE `
  -DeadLetterDecision FORMAL_ARCHIVE `
  -CapacityDecision FORMAL_ACCEPT_LIMITED_CAPACITY `
  -StabilityDecision CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA `
  -PublicGaDecision KEEP_LIMITED_GA `
  -MaxStores 2 `
  -MaxConcurrentTerminals 2 `
  -MinCompletedSalesInLast24h 6 `
  -MinPaymentsInLast24h 6 `
  -MinReceiptsIssuedInLast24h 3 `
  -MinAuditEventsInLast24h 1 `
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
  -SkipLga08Revalidation
```

Use `-SkipLga08Revalidation` only because LGA-08 already has reviewed PASS production logs. Remove the switch if the LGA-08 runtime manifest is present locally and you want prerequisite manifest enforcement.

## Expected current PASS

```text
[LGA-09] PASS LGA-09 LIMITED GA STABILITY CONFIRMATION / CAPACITY UPGRADE REQUIRED BEFORE PUBLIC GA
```

If infrastructure was truly upgraded and the capacity probe passes, `-StabilityDecision CONTINUE_LIMITED_GA` may be used instead.

## If it fails

Send the complete PowerShell output beginning at `[LGA-09] Validator version` through the blocker/exception, including the final probe and manifest values. Do not send passwords, JWTs, `DATABASE_URL`, access tokens or Authorization headers.
