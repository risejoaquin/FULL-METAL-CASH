# HOTFIX LGA-04.3 — Formal Limited Capacity Acceptance Contract Alignment

## Objetivo
Permitir cierre formal de LGA-04 bajo Limited GA cuando el capacity probe no pasa, siempre que la decisión explícita sea `FORMAL_ACCEPT_LIMITED_CAPACITY` y `KEEP_LIMITED_GA`.

## Rerun

```powershell
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
  -CapacityDecision FORMAL_ACCEPT_LIMITED_CAPACITY `
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

## Resultado esperado

```text
Validator version LGA-04.3-formal-limited-capacity-acceptance-contract
PASS LGA-04 LIMITED GA PUBLIC GA DECISION READINESS / KEEP LIMITED GA - FORMAL LIMITED CAPACITY ACCEPTED
publicGaActivation = NOT_ACTIVATED
capacityDecision = FORMAL_ACCEPT_LIMITED_CAPACITY
```
