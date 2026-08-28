# GA-08.8 validation commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1
Unblock-File .\scripts\ga\validate-ga-07-backup-restore-rollback-disaster-recovery.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

Select-String .\scripts\ga\validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1 -Pattern "GA-08.8-tenant-scoped-refresh-rls-session"

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString

.\scripts\ga\validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SecretsRotatedAfterExposure `
  -SkipDashboardBuild
```

Expected auth gate:

```text
[GA-08] Authentication subcheck login PASS
[GA-08] Authentication subcheck JWT claims PASS
[GA-08] Authentication subcheck refresh rotation PASS
[GA-08] Authentication subcheck old refresh reuse rejected PASS
[GA-08] Authentication subcheck logout PASS
[GA-08] Authentication subcheck logged-out refresh rejected PASS
```
