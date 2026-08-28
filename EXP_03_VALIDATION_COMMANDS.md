# SolidPOS EXP-03 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)

Unblock-File .\scripts\expansion\validate-exp-03-second-terminal-production-expansion.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\expansion\validate-exp-03-second-terminal-production-expansion.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected result:

```text
[EXP-03] EXP-03 PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04
```


## HOTFIX 03.1 retry

Repeat the same EXP-03 validation command. The SQL cross-check now validates inventory ledger rows using `reference_type` and `reference_id`.

## HOTFIX 03.2 Validation

Repeat the same EXP-03 validation command after applying HOTFIX 03.2:

```powershell
.\scripts\expansion\validate-exp-03-second-terminal-production-expansion.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected result:

```text
[EXP-03] SQL second terminal production expansion cross-check PASS
[EXP-03] EXP-03 PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04
```

## HOTFIX 03.3

Corrige el SQL cross-check final de EXP-03 para no depender de `pos.inventory_current`. El contrato real de stock se valida desde `pos.inventory_ledger` y `pos.inventory_stock`.

Repetir:

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\expansion\validate-exp-03-second-terminal-production-expansion.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\expansion\validate-exp-03-second-terminal-production-expansion.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```
