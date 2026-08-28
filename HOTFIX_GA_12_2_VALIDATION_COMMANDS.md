# Hotfix GA-12.2 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-ga-12-final-general-availability-launch-readiness.ps1
Unblock-File .\scripts\ga\validate-ga-11-customer-operator-admin-acceptance.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

Select-String .\scripts\ga\validate-ga-12-final-general-availability-launch-readiness.ps1 -Pattern "GA-12.2-go-nogo-contract-keyword-alignment"

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString

.\scripts\ga\validate-ga-12-final-general-availability-launch-readiness.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild `
  -SkipGa11Revalidation
```

Expected:

```text
[GA-12] PASS GENERAL AVAILABILITY READINESS / GO CONTROLLED GA ROLLOUT
```
