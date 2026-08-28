# SolidPOS PILOT-01 Validation Commands — Controlled Store Pilot Setup

## 1. Restore

```powershell
dotnet restore solidpos-platform.sln
```

Expected:

```text
Todos los proyectos están actualizados para la restauración.
```

## 2. Build

```powershell
dotnet build solidpos-platform.sln
```

Expected:

```text
Compilación correcta.
0 Advertencia(s)
0 Errores
```

## 3. Tests

```powershell
dotnet test solidpos-platform.sln
```

Expected:

```text
SolidPOS.PosCore.UnitTests            PASS
SolidPOS.PosServer.UnitTests          PASS
SolidPOS.PosServer.IntegrationTests   PASS
SolidPOS.PosServer.ContractTests      PASS
```

## 4. Set production DATABASE_URL

Use the current Supabase/PostgreSQL connection string. Do not paste it into chat or commit it to Git.

```powershell
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
```

Expected quick check:

```powershell
$env:DATABASE_URL.Substring(0,13)
```

Expected:

```text
postgresql://
```

## 5. Set secure admin password

```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"
```

## 6. Run PILOT-01 setup validation

```powershell
.\scripts\pilot\validate-controlled-store-pilot-setup.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -StoreCode "MAIN" `
  -ProductSku "QSR-AMERICANO" `
  -PaymentMethodCode "cash"
```

Expected:

```text
[PILOT-01] Local repository guardrails PASS
[PILOT-01] Local secret scan PASS
[PILOT-01] PosDashboard production build and self-test PASS
[PILOT-01] Production liveness PASS
[PILOT-01] Production readiness PASS
[PILOT-01] Admin login PASS
[PILOT-01] Protected metrics PASS
[PILOT-01] Sync runtime status PASS
[PILOT-01] Sales read model availability PASS
[PILOT-01] Audit events read model availability PASS
[PILOT-01] Controlled store data setup via PostgreSQL PASS
[PILOT-01] Pilot daily log initialized PASS

controlledStoreSetup : passed
goNoGo               : GO
message              : SolidPOS PILOT-01 controlled store pilot setup completed.
```

## Stop conditions

Do not continue to PILOT-02 if any step fails.

Send the failing block exactly as shown in PowerShell if one of these fails:

- `dotnet build`
- `dotnet test`
- dashboard build/self-test
- `/health/ready`
- admin login
- PostgreSQL setup check
- terminal/store/product/payment validation

## Hotfix 01.1 validation note

If `pilot-01-store-setup-check.sql` previously printed a `GO` row and then failed with:

```text
ERROR: syntax error at or near ":"
```

apply Hotfix 01.1 and rerun the same PILOT-01 command. The SQL now uses `pg_temp.pilot_01_state` so psql variables are not referenced inside a dollar-quoted `DO` block.


## Hotfix 01.3

If Hotfix 01.2 fails with `relation "pg_temp.pilot_01_state" does not exist`, apply Hotfix 01.3 and re-run only:

```powershell
.\scripts\pilotalidate-controlled-store-pilot-setup.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -StoreCode "MAIN" `
  -ProductSku "QSR-AMERICANO" `
  -PaymentMethodCode "cash"
```
