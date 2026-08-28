# PILOT-10 Validation Commands

## 1. Go to repo

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
```

## 2. Load secrets

```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```

Expected:

```text
postgresql://
```

## 3. Unblock scripts

```powershell
Unblock-File .\scripts\pilot\validate-pilot-closure-production-expansion.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

## 4. Run PILOT-10

```powershell
.\scripts\pilot\validate-pilot-closure-production-expansion.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL
```

## 5. Faster option

Use this when dashboard build was already validated in PILOT-07:

```powershell
.\scripts\pilot\validate-pilot-closure-production-expansion.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

## Expected result

```text
[PILOT-10] PILOT-10 PASS REAL PRODUCTION / GO
```

## Logs if it fails

Send:

```text
PowerShell output from the first [PILOT-10]
docs/pilot/logs/pilot-10-closure-production-expansion-log.md if it exists
scripts/pilot/validate-pilot-closure-production-expansion.ps1
scripts/pilot/pilot-10-production-expansion-check.sql
```


## HOTFIX 10.1

Repeat the same validation command after applying the ZIP.


## HOTFIX 10.2

Re-run the same validation command after applying the ZIP. This hotfix only corrects the GO/NO-GO risk documentation contract.


## HOTFIX 10.3 retry

Repeat the same command after applying the hotfix. Expected immediate step: `[PILOT-10] SQL production expansion cross-check PASS`.


## HOTFIX 10.4

Repeat the same PILOT-10 validation command after applying the ZIP. The SQL cross-check now uses `pos.return_refunds`, matching the real schema validated in PILOT-04.
