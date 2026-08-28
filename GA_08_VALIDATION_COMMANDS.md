# GA-08 Validation Commands

## 0. Obligatorio antes de ejecutar

La credencial de base de datos expuesta en el log anterior debe haber sido rotada y el deployment debe estar usando la nueva credencial.

No pegues la nueva cadena de conexión en el chat ni la imprimas en consola.

Carga localmente la nueva `DATABASE_URL` mediante el método seguro que estés usando y valida solo presencia:

```powershell
[bool]$env:DATABASE_URL
```

Debe ser `True`.

## 1. Restore

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

dotnet restore .\solidpos-platform.sln
```

## 2. Build

```powershell
dotnet build .\solidpos-platform.sln --no-restore
```

## 3. Tests

```powershell
dotnet test .\solidpos-platform.sln --no-build
```

Los tests GA-08 nuevos deben quedar PASS, incluidos:

- cobertura RLS para todas las tablas con `tenant_id`;
- rechazo de write cross-tenant por RLS;
- permiso ausente no satisface `PermissionRequirement`.

## 4. Migración GA-08

No usar `-ResetSchema`.

```powershell
.\scripts\apply-postgresql-migrations.ps1 `
  -DatabaseUrl $env:DATABASE_URL
```

Debe aplicar:

```text
database/postgresql/020_ga08_complete_tenant_rls.sql
```

## 5. Health después de rotación/migración

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri "https://full-metal-cash-production.up.railway.app/health/ready"
```

Esperado: `status=ready`, `database=ready`.

## 6. Password

```powershell
$securePassword = Read-Host "Password SolidPOS" -AsSecureString
```

## 7. Ejecutar GA-08

```powershell
Unblock-File .\scripts\ga\validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\ga\validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SecretsRotatedAfterExposure `
  -SkipDashboardBuild
```

## Checkpoints esperados

```text
[GA-08] Repository/document/source GA-08 guardrails PASS
[GA-08] Secret scan PASS
[GA-08] Fresh GA-07 prerequisite revalidation PASS
[GA-08] GA-08 PostgreSQL security/RLS source-of-truth PASS
[GA-08] Production security headers, Swagger policy and CORS negative test PASS
[GA-08] Authentication login / JWT claims / refresh rotation / reuse / logout PASS
[GA-08] Negative authentication / authorization policy gate PASS
[GA-08] Cross-tenant API isolation negative reads PASS
[GA-08] Provisioning isolation and status contract PASS
[GA-08] GA-08 authentication session cleanup PASS
[GA-08] Append GA-08 security audit evidence PASS
[GA-08] GA-08 evidence manifest and security snapshot PASS
```

Cierre exacto:

```text
[GA-08] GA-08 PASS GA SECURITY TENANT ISOLATION ACCESS CONTROL / GO GA-09
```

Si falla cualquier gate, detener y enviar el log completo. No avanzar a GA-09.
