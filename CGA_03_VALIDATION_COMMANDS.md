# CGA-03 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-cga-03-capacity-db-remediation-or-formal-acceptance.ps1
Unblock-File .\scripts\ga\validate-cga-02-production-monitoring-incident-window.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

Select-String .\scripts\ga\validate-cga-03-capacity-db-remediation-or-formal-acceptance.ps1 -Pattern "CGA-03.1-observability-p95-metric-compatibility"

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString

if ([string]::IsNullOrWhiteSpace($env:DATABASE_URL)) {
  throw "DATABASE_URL no está configurado en esta sesión."
} else {
  "DATABASE_URL presente en sesión: OK"
}

.\scripts\ga\validate-cga-03-capacity-db-remediation-or-formal-acceptance.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -RolloutMode LIMITED `
  -Decision FORMAL_ACCEPTANCE `
  -MaxStores 2 `
  -MaxConcurrentTerminals 2 `
  -CapacitySampleCount 12 `
  -MaxP95LatencyMs 5000 `
  -AllowedExistingSyncConflictCount 3 `
  -AllowedDeadLetterCount 1 `
  -AllowedWaitingConnectionCount 11 `
  -SkipDashboardBuild `
  -SkipCga02Revalidation
```

Expected final line:

```text
[CGA-03] PASS CGA-03 FORMAL LIMITED CAPACITY ACCEPTANCE / GO CGA-04
```
