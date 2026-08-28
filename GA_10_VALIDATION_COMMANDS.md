# GA-10 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-ga-10-observability-dashboard-alerting-oncall-readiness.ps1
Unblock-File .\scripts\ga\validate-ga-09-performance-capacity-resilience-offline-readiness.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

Select-String .\scripts\ga\validate-ga-10-observability-dashboard-alerting-oncall-readiness.ps1 -Pattern "GA-10.2-db-json-output-parser-guardrail"

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString

.\scripts\ga\validate-ga-10-observability-dashboard-alerting-oncall-readiness.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild `
  -SkipGa09Revalidation
```

Use `-SkipGa09Revalidation` only because GA-09 PASS real logs already exist in this chat. Remove it if a fresh GA-09 revalidation is desired.
