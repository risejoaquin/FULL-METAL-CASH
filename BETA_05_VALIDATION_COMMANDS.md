# BETA-05 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```

Expected: `postgresql://`

```powershell
Unblock-File .\scripts\beta\validate-beta-05-support-operations-drill.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\beta\validate-beta-05-support-operations-drill.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected final line:
`[BETA-05] PASS BETA SUPPORT OPERATIONS DRILL / GO BETA-06`

If it fails, send the log beginning at the first `[BETA-05]` or `[EXP-08]` failure through the complete exception.
