# LGA-08 Validation Commands

## 1. Prepare secrets locally

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString
$env:DATABASE_URL = Read-Host "DATABASE_URL"
```

## 2. Unblock validators

```powershell
Unblock-File .\scripts\ga\validate-lga-08-limited-ga-post-upgrade-verification-or-continued-monitoring.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1
```

## 3. Restore

```powershell
dotnet restore .\solidpos-platform.sln
```

Expected: exit code 0.

## 4. Build

```powershell
dotnet build .\solidpos-platform.sln --no-restore
```

Expected: build succeeds with zero errors.

## 5. Tests

```powershell
dotnet test .\solidpos-platform.sln --no-build
```

Expected: all configured test projects PASS.

## 6. Secret scan

```powershell
.\scripts\security\scan-local-secrets.ps1 -Root .
```

Expected: PASS; no secrets printed or committed.

## 7. SQL snapshot only (optional diagnostic)

The LGA-08 validator executes the SQL automatically. Run it manually only for diagnostics, using `psql` with variables rather than embedding credentials in files.

```powershell
psql $env:DATABASE_URL -X -q -tA -P footer=off -v ON_ERROR_STOP=1 `
  -v tenant_id="0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -v max_stores=2 `
  -v max_concurrent_terminals=2 `
  -v allowed_existing_sync_conflicts=3 `
  -v allowed_dead_letters=1 `
  -v allowed_waiting_connections=12 `
  -f .\scripts\ga\lga-08-limited-ga-post-upgrade-verification-or-continued-monitoring-check.sql
```

Expected: one `LGA08_JSON:` payload with schemaVersion 4, negative stock 0 and Public GA flags false.

## 8. LGA-08 — continued monitoring (current authorized path)

```powershell
.\scripts\ga\validate-lga-08-limited-ga-post-upgrade-verification-or-continued-monitoring.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ConflictDecision FORMAL_ARCHIVE `
  -DeadLetterDecision FORMAL_ARCHIVE `
  -CapacityDecision FORMAL_ACCEPT_LIMITED_CAPACITY `
  -VerificationMode CONTINUED_MONITORING `
  -PublicGaDecision KEEP_LIMITED_GA `
  -MaxStores 2 `
  -MaxConcurrentTerminals 2 `
  -MinCompletedSalesInLast24h 6 `
  -MinPaymentsInLast24h 6 `
  -MinReceiptsIssuedInLast24h 3 `
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
  -SkipLga07Revalidation
```

Expected final line:

```text
[LGA-08] PASS LGA-08 LIMITED GA POST-UPGRADE VERIFICATION OR CONTINUED MONITORING / CONTINUE LIMITED GA
```

## 9. Post-upgrade verification (only after an external Railway capacity change)

Do not use this mode unless capacity was actually upgraded outside the validator.

```powershell
.\scripts\ga\validate-lga-08-limited-ga-post-upgrade-verification-or-continued-monitoring.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ConflictDecision FORMAL_ARCHIVE `
  -DeadLetterDecision FORMAL_ARCHIVE `
  -CapacityDecision REMEDIATE_BEFORE_PUBLIC_GA `
  -VerificationMode POST_UPGRADE_VERIFICATION `
  -PublicGaDecision KEEP_LIMITED_GA `
  -AllowedWaitingConnectionCount 12 `
  -PublicGaReadinessConcurrency 3 `
  -ConcurrencyProbeRequests 6 `
  -MaxReadinessP95Ms 1200 `
  -WpfVisualConfirmed `
  -SkipDashboardBuild `
  -SkipLga07Revalidation
```

Expected only if the upgrade is truly healthy:

```text
[LGA-08] PASS LGA-08 POST-UPGRADE CAPACITY VERIFICATION / KEEP LIMITED GA - PUBLIC GA NOT ACTIVATED
```

## Logs to send if LGA-08 fails

Send the complete console output from the first `[LGA-08]` line through the exception/blocker JSON and the newest JSON manifest under:

```text
.runtime\lga-08-limited-ga-post-upgrade-verification-or-continued-monitoring\
```

Do not send passwords, JWTs, `DATABASE_URL`, Authorization headers, or other secrets.
