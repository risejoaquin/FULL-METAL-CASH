# EXP-06 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)

Unblock-File .\scripts\expansion\validate-exp-06-inventory-reconciliation-hardening.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\expansion\validate-exp-06-inventory-reconciliation-hardening.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

## Dry run opcional

```powershell
.\scripts\expansion\validate-exp-06-inventory-reconciliation-hardening.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild `
  -DryRun
```

DryRun valida pero no aplica ajustes. Para cerrar EXP-06 con negative inventory existente, no uses DryRun.

## Resultado esperado

```text
[EXP-06] EXP-06 PASS INVENTORY RECONCILIATION HARDENING / GO EXP-07
```


## HOTFIX 06.1 note

Repeat the same EXP-06 validation command after applying the hotfix. The document contract now accepts explicit append-only ledger wording and equivalent phrasing.
