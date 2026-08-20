# SolidPOS PILOT-03 — Validation Commands

## Scope

PILOT-03 validates cash drawer and shift operations in production after PILOT-01 and PILOT-02 are already GO.

Validated flow:

1. Local repository guardrails.
2. Local secret scan.
3. PosDashboard production build and self-test.
4. Production liveness/readiness.
5. Admin login.
6. Protected metrics.
7. Production lookup for store/admin/product/price.
8. Terminal enrollment/register.
9. Stale PILOT-03 open shift cleanup.
10. Shift open.
11. Current open shift read.
12. Cash in movement.
13. Cash out movement.
14. Drawer open without sale movement.
15. Shift summary before sales.
16. Two controlled cash sales in same shift.
17. Shift summary after sales.
18. Shift close with zero difference.
19. Cash audit trail.
20. PostgreSQL persistence validation.
21. Pilot cash drawer log.

## Restore

```powershell
dotnet restore solidpos-platform.sln
```

## Build

```powershell
dotnet build solidpos-platform.sln
```

Expected:

```text
Compilación correcta.
0 Advertencia(s)
0 Errores
```

## Test

```powershell
dotnet test solidpos-platform.sln
```

Expected:

```text
Correctas! - Con error: 0
```

## DATABASE_URL

```powershell
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```

Expected:

```text
postgresql://
```

## Production admin password

```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"
```

## Run PILOT-03

```powershell
.\scripts\pilot\validate-cash-drawer-shift-operations.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -StoreCode "MAIN" `
  -ProductSku "QSR-AMERICANO" `
  -PaymentMethodCode "cash"
```

## Expected final output

```text
[PILOT-03] Local repository guardrails PASS
[PILOT-03] Local secret scan PASS
[PILOT-03] PosDashboard production build and self-test PASS
[PILOT-03] Production liveness PASS
[PILOT-03] Production readiness PASS
[PILOT-03] Admin login PASS
[PILOT-03] Protected metrics PASS
[PILOT-03] Production cash drawer data lookup via PostgreSQL PASS
[PILOT-03] Terminal enrollment/register PASS
[PILOT-03] Closing stale open PILOT-03 cash shifts PASS
[PILOT-03] Opening cash shift PASS
[PILOT-03] Validating current open shift PASS
[PILOT-03] Creating cash movements PASS
[PILOT-03] Validating movement summary before sales PASS
[PILOT-03] Creating controlled cash sales for shift accumulation PASS
[PILOT-03] Validating shift summary after sales PASS
[PILOT-03] Closing cash shift with zero difference PASS
[PILOT-03] Validating cash audit trail PASS
[PILOT-03] Validating cash drawer persistence via PostgreSQL PASS
[PILOT-03] Pilot cash drawer log initialized PASS

goNoGo : GO
message : SolidPOS PILOT-03 cash drawer and shift operations validation completed.
```

## Logs to send if it fails

Send the complete PowerShell output from the first failing `[PILOT-03]` step and the final exception block.

Do not continue to PILOT-04 if one of these fails:

- shift open
- cash in / cash out / no-sale drawer movement
- summary expected cash
- sales accumulation
- shift close
- difference zero
- audit trail
- SQL persistence
