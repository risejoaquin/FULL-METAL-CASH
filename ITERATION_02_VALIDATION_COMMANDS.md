# Iteration 02 — Validation Commands

## Local

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```

## Migraciones locales

```powershell
.\scripts\apply-postgresql-migrations.ps1
```

## Migración 017 en Supabase real

```powershell
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"

docker run --rm `
  --env "DATABASE_URL=$env:DATABASE_URL" `
  -v "${PWD}:/work" `
  -w /work `
  postgres:16 `
  psql "$env:DATABASE_URL" -v ON_ERROR_STOP=1 -f database/postgresql/017_pos_operational_completion.sql
```

## Verificar columnas e índice operativo

```powershell
docker run --rm `
  --env "DATABASE_URL=$env:DATABASE_URL" `
  postgres:16 `
  psql "$env:DATABASE_URL" -c "select column_name from information_schema.columns where table_schema = 'pos' and table_name = 'cash_movements' and column_name in ('occurred_at','local_occurred_at','metadata') order by column_name;"
```

## Smoke remoto con admin productivo

```powershell
.\scripts\smoke-test-deployment.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -Email "admin@micafeteria.com" `
  -Password "AdminSeguro123!" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde"
```

## Seed POS runtime para tenant productivo

```powershell
.\scripts\operations\seed-production-pos-runtime.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Currency "MXN"
```

## Validación E2E venta/caja/recibo/corte

```powershell
.\scripts\operations\validate-production-pos-e2e.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```


## Hotfix 02.1

Se agregó `sales.read` al permiso default de terminal para permitir que el flujo POS productivo emita recibo digital usando el token de terminal sin fallar con `403 Forbidden` en `POST /api/v1/receipts/{saleId}/issue`.

## Hotfix 02.2 — E2E stale shift recovery

If the E2E script previously failed after opening a cash shift, the next run may hit `409 Conflict` because the same deterministic E2E terminal still has an open shift. Hotfix 02.2 makes the validation script close stale open E2E shifts automatically before opening a new one.

Default behavior:

```powershell
.\scripts\operations\validate-production-pos-e2e.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```

Disable automatic stale shift cleanup only when debugging the open-shift conflict itself:

```powershell
.\scripts\operations\validate-production-pos-e2e.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!" `
  -CloseStaleOpenShifts $false
```
