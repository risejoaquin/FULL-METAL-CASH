# PILOT-09 Validation Commands

## 1. Prepare variables

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```

Expected:

```text
postgresql://
```

## 2. Unblock scripts

```powershell
Unblock-File .\scripts\pilot\validate-pilot-incident-runbook.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

## 3. Run PILOT-09

```powershell
.\scripts\pilot\validate-pilot-incident-runbook.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL
```

Optional if dashboard build was already validated locally and you only want runbook/API/SQL validation:

```powershell
.\scripts\pilot\validate-pilot-incident-runbook.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

## Expected result

```text
[PILOT-09] PILOT-09 PASS REAL PRODUCTION / GO
```

## Logs if it fails

Send:

```text
Full PowerShell output from first [PILOT-09]
docs/pilot/logs/pilot-09-incident-runbook-log.md if generated
scripts/pilot/validate-pilot-incident-runbook.ps1
scripts/pilot/pilot-09-incident-runbook-check.sql
```
