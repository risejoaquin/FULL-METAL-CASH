# HOTFIX GA-08.11 — Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1
Unblock-File .\scripts\ga\validate-ga-07-backup-restore-rollback-disaster-recovery.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

Select-String .\scripts\ga\validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1 -Pattern "GA-08.11-powershell-assertion-type-safety"

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

Expected:

```text
[GA-08] Authentication login / JWT claims / refresh rotation / reuse / logout PASS
[GA-08] Cross-tenant API isolation negative reads PASS
[GA-08] GA-08 PASS GA SECURITY TENANT ISOLATION ACCESS CONTROL / GO GA-09
```
