# HOTFIX LGA-04.1 Validation Commands

## Context
Hotfix for LGA-04 validator contract alignment. Public GA remains not activated.

## Run
```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-lga-04-public-ga-decision-readiness-capacity-remediation.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

.\scripts\ga\validate-lga-04-public-ga-decision-readiness-capacity-remediation.ps1 `
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
  -AllowedExistingSyncConflictCount 3 `
  -AllowedDeadLetterCount 1 `
  -AllowedNegativeStockItemCount 0 `
  -AllowedOpenShiftCount 0 `
  -AllowedWaitingConnectionCount 11 `
  -PublicGaReadinessConcurrency 3 `
  -ConcurrencyProbeRequests 6 `
  -MaxReadinessP95Ms 1200 `
  -WpfVisualConfirmed `
  -SkipDashboardBuild
```

## Expected
```text
[LGA-04] Validator version LGA-04.1-sync-contract-schema-source-alignment
[LGA-04] Limited GA public decision API checks PASS
[LGA-04] Database Limited GA public decision snapshot PASS
```

Expected final status is one of:

```text
PASS LGA-04 LIMITED GA PUBLIC GA DECISION READINESS / KEEP LIMITED GA - CAPACITY REMEDIATION REQUIRED
```

or:

```text
PASS LGA-04 LIMITED GA PUBLIC GA DECISION READINESS / KEEP LIMITED GA
```
