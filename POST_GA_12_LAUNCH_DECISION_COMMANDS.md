# Post-GA-12 Launch Decision Commands

Run from repo root:

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-post-ga-12-launch-decision.ps1
Unblock-File .\scripts\ga\validate-ga-12-final-general-availability-launch-readiness.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

Select-String .\scripts\ga\validate-post-ga-12-launch-decision.ps1 -Pattern "POST-GA-12.0-launch-decision-control-plane"

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString

.\scripts\ga\validate-post-ga-12-launch-decision.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -Decision CONTROLLED_ROLLOUT `
  -SkipDashboardBuild `
  -SkipGa12Revalidation
```

Expected result:

```text
[POST-GA-12] Repository/document Post-GA-12 guardrails PASS
[POST-GA-12] Local build/test/secret guardrails PASS
[POST-GA-12] Production post-GA-12 decision API checks PASS
[POST-GA-12] Database post-GA-12 decision snapshot PASS
[POST-GA-12] Post-GA-12 launch decision matrix PASS
[POST-GA-12] Post-GA-12 evidence manifest and launch decision snapshot PASS
[POST-GA-12] PASS POST-GA-12 CONTROLLED ROLLOUT DECISION / READY FOR LIMITED GA EXECUTION
```
