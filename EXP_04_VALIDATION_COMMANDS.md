# EXP-04 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)

Unblock-File .\scripts\expansion\validate-exp-04-second-store-limited-expansion.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\expansion\validate-exp-04-second-store-limited-expansion.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected result:

```text
[EXP-04] EXP-04 PASS SECOND STORE LIMITED EXPANSION / GO EXP-05
```
