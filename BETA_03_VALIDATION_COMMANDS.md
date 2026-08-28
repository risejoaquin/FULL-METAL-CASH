# BETA-03 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```

```powershell
Unblock-File .\scripts\beta\validate-beta-03-store-operations-validation.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

```powershell
.\scripts\beta\validate-beta-03-store-operations-validation.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected terminal line:
`[BETA-03] BETA-03 PASS BETA STORE OPERATIONS VALIDATION / GO BETA-04`
