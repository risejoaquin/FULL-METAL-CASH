# Post-LGA Capacity / Infrastructure Remediation Commands

## 1. Local regression gate

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

dotnet restore solidpos-platform.sln
dotnet build solidpos-platform.sln --no-restore
dotnet test solidpos-platform.sln --no-build

Unblock-File .\scripts\security\scan-local-secrets.ps1
.\scripts\security\scan-local-secrets.ps1 -Root .
```

Expected: build PASS, all test suites PASS, secret scan clean.

## 2. Deploy the PosServer change

Commit and push the repository through the existing Railway deployment pipeline after local regression passes.

```powershell
git status
git add .
git commit -m "Optimize PostgreSQL readiness capacity path"
git push
```

Confirm the Railway deployment is healthy before running the strict capacity validator.

## 3. Prepare production validation inputs

```powershell
$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString
$env:DATABASE_URL = Read-Host "DATABASE_URL"

Unblock-File .\scripts\ga\validate-post-lga-capacity-infrastructure-remediation.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1
```

## 4. Execute strict post-LGA capacity gate

```powershell
.\scripts\ga\validate-post-lga-capacity-infrastructure-remediation.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ConflictDecision FORMAL_ARCHIVE `
  -DeadLetterDecision FORMAL_ARCHIVE `
  -CapacityDecision REMEDIATE_BEFORE_PUBLIC_GA `
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
  -SkipLga12Revalidation
```

Expected strict PASS:

```text
[POST-LGA-CAPACITY] PASS POST-LGA CAPACITY INFRASTRUCTURE REMEDIATION / CAPACITY GATE PASSED / LIMITED GA RETAINED / PUBLIC GA NOT ACTIVATED
```

If it fails on `public_ga_capacity_gate_not_met_after_remediation`, do not change thresholds. The next action is Railway resource scaling / PostgreSQL pool-pressure review, then rerun this same validator.

Send the full output from `[POST-LGA-CAPACITY] Validator version` through the final blocker/manifest. Do not send passwords, JWTs or `DATABASE_URL`.
