# BETA-04 Validation Commands

## 1. Secrets / database
```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```
Expected: `postgresql://`

## 2. Unblock validator
```powershell
Unblock-File .\scripts\beta\validate-beta-04-offline-reliability-field-run.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

## 3. Run BETA-04
```powershell
.\scripts\beta\validate-beta-04-offline-reliability-field-run.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected final line:
`[BETA-04] BETA-04 PASS BETA OFFLINE RELIABILITY FIELD RUN / GO BETA-05`

If it fails, send the complete output starting at the first failing `[BETA-04]` or nested `[PILOT-05]` step through the exception.
