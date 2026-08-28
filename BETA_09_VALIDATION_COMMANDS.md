# BETA-09 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```

Expected: `postgresql://`

```powershell
Unblock-File .\scripts\beta\validate-beta-09-data-quality-reconciliation-closure.ps1
Unblock-File .\scripts\beta\validate-beta-08-customer-acceptance-validation.ps1
Unblock-File .\scripts\beta\validate-beta-07-dashboard-daily-monitoring-pack.ps1
Unblock-File .\scripts\expansion\validate-exp-06-inventory-reconciliation-hardening.ps1
Unblock-File .\scripts\expansion\validate-exp-05-operational-monitoring-hardening.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

```powershell
.\scripts\beta\validate-beta-09-data-quality-reconciliation-closure.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected final line:

`[BETA-09] BETA-09 PASS BETA DATA QUALITY RECONCILIATION CLOSURE / GO BETA-10`

If the run fails, send the first failing `[BETA-09]`, `[BETA-08]`, `[BETA-07]`, `[EXP-06]`, or `[EXP-05]` step through the complete exception and SQL blocker list.
