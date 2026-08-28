# GA-11 Validation Commands — Customer, Operator and Admin Acceptance

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-ga-11-customer-operator-admin-acceptance.ps1
Unblock-File .\scripts\ga\validate-ga-10-observability-dashboard-alerting-oncall-readiness.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

Select-String .\scripts\ga\validate-ga-11-customer-operator-admin-acceptance.ps1 -Pattern "GA-11.0-customer-operator-admin-acceptance"

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString

.\scripts\ga\validate-ga-11-customer-operator-admin-acceptance.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild `
  -SkipGa10Revalidation
```

## Expected result

```text
[GA-11] GA-11 evidence manifest and acceptance snapshot PASS
[GA-11] GA-11 PASS GA CUSTOMER OPERATOR ADMIN ACCEPTANCE / GO GA-12
```

Do not paste passwords, tokens, refresh tokens, or DATABASE_URL.
