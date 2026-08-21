# EXP-01 Validation Commands

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
Unblock-File .\scripts\expansion\validate-exp-01-post-pilot-baseline-freeze.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

## 4. Run EXP-01

```powershell
.\scripts\expansion\validate-exp-01-post-pilot-baseline-freeze.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL
```

## 5. Faster option

Use this only if the Dashboard build was already validated and you only want the baseline repo/server validation:

```powershell
.\scripts\expansion\validate-exp-01-post-pilot-baseline-freeze.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

## Expected result

```text
[EXP-01] EXP-01 PASS POST-PILOT BASELINE FREEZE / GO EXP-02
```

## Expected artifacts

```text
docs/expansion/logs/exp-01-post-pilot-baseline-freeze-log.md
.runtime/exp-01-post-pilot-baseline-freeze/baseline-manifest.json
```

## Logs if it fails

Send:

```text
PowerShell output from the first [EXP-01]
docs/expansion/logs/exp-01-post-pilot-baseline-freeze-log.md if it exists
.runtime/exp-01-post-pilot-baseline-freeze/baseline-manifest.json if it exists
scripts/expansion/validate-exp-01-post-pilot-baseline-freeze.ps1
```

## Recommended release tag after PASS

```powershell
git add .
git commit -m "EXP-01 post-pilot baseline freeze"
git tag v0.10.0-post-pilot.20260820
git push
git push origin v0.10.0-post-pilot.20260820
```
