# BETA-01 Validation Commands

## 1. Open repository
```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
```

## 2. Load production credentials without printing secrets
```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```
Expected:
```text
postgresql://
```

## 3. Unblock validators
```powershell
Unblock-File .\scripts\beta\validate-beta-01-controlled-commercial-beta-onboarding.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

## 4. Execute BETA-01 without dashboard build
```powershell
.\scripts\beta\validate-beta-01-controlled-commercial-beta-onboarding.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

## 5. Optional full dashboard gate
Remove `-SkipDashboardBuild` and rerun the same command when dashboard validation is required.

## Expected final result
```text
[BETA-01] BETA-01 PASS CONTROLLED COMMERCIAL BETA ONBOARDING / GO BETA-02
```
The returned manifest must show `blockers` empty, `schemaVersion = 4`, `syncContract = schema_version_4`, an active admin with role/store access, active customer/catalog/pricing, release evidence, zero pending conflicts and zero legacy schema events.

## If it fails
Send the complete console output beginning at the first `[BETA-01]` step that fails through the exception. Also send the generated `.runtime\beta-01-controlled-commercial-beta-onboarding\beta-01-onboarding-manifest.json` only if it exists and contains no secrets. Do not send passwords, JWTs or the full DATABASE_URL.
