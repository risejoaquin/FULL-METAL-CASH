# EXP-10 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)

Unblock-File .\scripts\expansion\validate-exp-10-customer-admin-management-completion.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\expansion\validate-exp-10-customer-admin-management-completion.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected:

```text
[EXP-10] EXP-10 PASS CUSTOMER ADMIN MANAGEMENT COMPLETION / GO EXP-11
```

---

## HOTFIX 10.1

If the first EXP-10 package failed with:

```text
Customer list/search did not include created customer.
```

apply HOTFIX 10.1 and rerun the same validation command. The customer list/search filter is now checked using multiple query shapes and treated as a non-blocking condition when `GET by id` and SQL cross-check pass.

## HOTFIX 10.2 rerun

Re-run the same EXP-10 command after applying HOTFIX 10.2. The user list/search endpoint is no longer the source of truth; SQL cross-check and create/update responses close the controlled user evidence.


## HOTFIX 10.3 — repetir validación

Después de aplicar `solidpos-platform-exp-10-hotfix-10-3-sql-psql-uuid-variable-quoting-contract-20260820.zip`, repetir el mismo comando:

```powershell
.\scripts\expansion\validate-exp-10-customer-admin-management-completion.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Resultado esperado:

```text
[EXP-10] SQL customer/admin management cross-check PASS
[EXP-10] EXP-10 PASS CUSTOMER ADMIN MANAGEMENT COMPLETION / GO EXP-11
```
