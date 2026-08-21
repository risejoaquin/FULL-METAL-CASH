# BETA-02 Validation Commands

## 1. Open repository
```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
```

## 2. Load secrets safely
```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```
Expected: `postgresql://`

## 3. Unblock scripts
```powershell
Unblock-File .\scripts\beta\validate-beta-02-tenant-provisioning-separation-hardening.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

## 4. Execute
```powershell
.\scripts\beta\validate-beta-02-tenant-provisioning-separation-hardening.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

## Expected result
`[BETA-02] BETA-02 PASS BETA TENANT PROVISIONING SEPARATION HARDENING / GO BETA-03`

## If it fails
Send the complete console output from the first failing `[BETA-02]` step onward. If generated, also send `.runtime\beta-02-tenant-provisioning-separation-hardening\beta-02-separation-manifest.json` after confirming it contains no secrets. Do not send passwords, JWTs, provisioning keys or the full DATABASE_URL.
