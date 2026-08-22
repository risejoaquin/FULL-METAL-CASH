# HOTFIX GA-01.1 — Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-ga-01-general-availability-baseline-freeze.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\ga\validate-ga-01-general-availability-baseline-freeze.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected final gate:

```text
[GA-01] GA-01 PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02
```
