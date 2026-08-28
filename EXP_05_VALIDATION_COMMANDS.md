# EXP-05 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)

Unblock-File .\scripts\expansion\validate-exp-05-operational-monitoring-hardening.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\expansion\validate-exp-05-operational-monitoring-hardening.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected:

```text
[EXP-05] EXP-05 PASS OPERATIONAL MONITORING HARDENING / GO EXP-06
```

Artifacts:

```text
docs/expansion/logs/exp-05-operational-monitoring-hardening-log.md
.runtime/exp-05-operational-monitoring-hardening/operational-monitoring-hardening-manifest.json
```


## HOTFIX 05.1 retry

Use the same command after applying `solidpos-platform-exp-05-hotfix-05-1-audit-events-occurred-at-contract-20260820.zip`.

Expected result:

```text
[EXP-05] SQL operational monitoring cross-check PASS
[EXP-05] EXP-05 PASS OPERATIONAL MONITORING HARDENING / GO EXP-06
```
