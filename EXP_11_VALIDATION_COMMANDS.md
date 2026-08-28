# EXP-11 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)

Unblock-File .\scripts\expansion\validate-exp-11-catalog-pricing-operations.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\expansion\validate-exp-11-catalog-pricing-operations.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected result:

```text
[EXP-11] EXP-11 PASS CATALOG PRICING OPERATIONS / GO EXP-12
```


## HOTFIX 11.1 Controlled Price List Bootstrap

EXP-11 now ensures a tenant-scoped active MXN price list before product price validation. The bootstrap is idempotent, reuses an existing active MXN price list when available, otherwise creates `EXP11-MXN`. It does not mutate sales, payments, cash drawer, inventory ledger, stores, terminals, sync, customers, users, release channels, or tenant identity.

## HOTFIX 11.2 command

Repeat the same EXP-11 validation command after applying `solidpos-platform-exp-11-hotfix-11-2-price-list-runtime-catalog-visibility-contract-20260820.zip`.

Expected PASS:

```text
[EXP-11] EXP-11 PASS CATALOG PRICING OPERATIONS / GO EXP-12
```
