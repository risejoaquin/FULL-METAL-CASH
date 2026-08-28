# Iteration 03 — Validation Commands

## 1. Validación local

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```

## 2. Migración local

```powershell
.\scripts\apply-postgresql-migrations.ps1
```

## 3. Migración remota Supabase

```powershell
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"

docker run --rm `
  --env "DATABASE_URL=$env:DATABASE_URL" `
  -v "${PWD}:/work" `
  -w /work `
  postgres:16 `
  psql "$env:DATABASE_URL" -v ON_ERROR_STOP=1 -f database/postgresql/018_sync_e2e_contract_hardening.sql
```

## 4. Smoke remoto con admin productivo

```powershell
.\scripts\smoke-test-deployment.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -Email "admin@micafeteria.com" `
  -Password "AdminSeguro123!" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde"
```

## 5. Validación E2E de sync

```powershell
.\scripts\sync\validate-sync-e2e-contract.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```

Resultado esperado:

```text
message : Offline sync E2E server contract completed.
```

## 6. GitHub

```powershell
git add .

git commit -m "Iteration 03 offline sync E2E server contract"

git push
```
