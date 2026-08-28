# CGA-02 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-cga-02-production-monitoring-incident-window.ps1
Unblock-File .\scripts\ga\validate-cga-01-controlled-ga-rollout-execution.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

Select-String .\scripts\ga\validate-cga-02-production-monitoring-incident-window.ps1 -Pattern "CGA-02.0-production-monitoring-incident-window"

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString

.\scripts\ga\validate-cga-02-production-monitoring-incident-window.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -RolloutMode LIMITED `
  -MaxStores 2 `
  -MaxConcurrentTerminals 2 `
  -MonitoringWindowHours 24 `
  -SampleCount 3 `
  -SampleIntervalSeconds 5 `
  -AllowedExistingSyncConflictCount 3 `
  -SkipDashboardBuild `
  -SkipCga01Revalidation
```

Expected:

```text
[CGA-02] Repository/document CGA-02 guardrails PASS
[CGA-02] Local build/test/secret guardrails PASS
[CGA-02] Production monitoring API samples PASS
[CGA-02] Database production monitoring snapshot PASS
[CGA-02] CGA-02 incident window and blocker matrix PASS
[CGA-02] CGA-02 evidence manifest and production monitoring snapshot PASS
[CGA-02] PASS CGA-02 PRODUCTION MONITORING INCIDENT WINDOW / GO CGA-03
```


## CGA-02.1 known conflict baseline

When CGA-02 is rerun after a documented operator test created known historical sync conflicts before the valid monitoring activity, use `-AllowedExistingSyncConflictCount <count>` to prevent the baseline from masking new blockers. The count must match the known existing conflicts and remains a carried condition, not a public GA approval.
