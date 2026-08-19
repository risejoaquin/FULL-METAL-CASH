# Iteration 01 — Comandos de validación

## 1. Validación local obligatoria

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```

No continúes si `build` o `test` fallan.

## 2. Migración local/remota

Local Docker:

```powershell
.\scripts\apply-postgresql-migrations.ps1
```

Remote DB usando `DATABASE_URL`:

```powershell
$env:DATABASE_URL = Read-Host "Pega DATABASE_URL"
.\scripts\apply-postgresql-migrations.ps1
```

## 3. Smoke remoto existente

```powershell
.\scripts\smoke-test-deployment.ps1 -BaseUrl "https://full-metal-cash-production.up.railway.app"
```

## 4. Bootstrap productivo

```powershell
$env:PROVISION_KEY = Read-Host "PROVISION_KEY"

.\scripts\provisioning\bootstrap-production-tenant.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -ProvisionKey "$env:PROVISION_KEY" `
  -TenantName "Mi Cafeteria" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminFullName "Admin Principal" `
  -AdminPassword "AdminSeguro123!" `
  -StoreCode "MAIN" `
  -StoreName "Sucursal Principal" `
  -IdempotencyKey "mi-cafeteria-bootstrap-v1"
```

## 5. Verificar login admin real

```powershell
.\scripts\provisioning\verify-production-bootstrap.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "<tenantId-del-bootstrap>" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```

## 6. Git

```powershell
git add .

git commit -m "Iteration 01 production tenant provisioning and admin bootstrap"

git push
```
