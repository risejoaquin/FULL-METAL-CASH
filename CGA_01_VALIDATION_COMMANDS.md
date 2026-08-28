# CGA-01 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-cga-01-controlled-ga-rollout-execution.ps1
Unblock-File .\scripts\ga\validate-post-ga-12-launch-decision.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

Select-String .\scripts\ga\validate-cga-01-controlled-ga-rollout-execution.ps1 -Pattern "CGA-01.0-controlled-ga-rollout-execution"

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString

.\scripts\ga\validate-cga-01-controlled-ga-rollout-execution.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -RolloutMode LIMITED `
  -MaxStores 2 `
  -MaxConcurrentTerminals 2 `
  -ObservationWindowHours 24 `
  -SkipDashboardBuild `
  -SkipPostGa12Revalidation
```

Expected final status:

`PASS CGA-01 CONTROLLED GA ROLLOUT EXECUTION / GO CGA-02`
