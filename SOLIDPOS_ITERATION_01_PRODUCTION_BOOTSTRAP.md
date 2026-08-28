# SolidPOS Iteration 01 — Production Tenant Provisioning + Admin Bootstrap

## Estado de entrega

**Estado:** listo para validación local del usuario.

Esta iteración reemplaza el modelo de macro fases para trabajar en entregas más grandes y funcionales. El objetivo es dejar de depender del usuario demo `owner@solidpos.local` para operación productiva y agregar un flujo controlado para crear tenant, store inicial y admin real.

## Objetivo funcional

Crear un flujo productivo, auditable e idempotente para:

- Crear tenant productivo.
- Crear configuración inicial de tenant.
- Crear store inicial.
- Sembrar roles MVP del tenant.
- Crear admin owner inicial con password policy real.
- Asignar rol `owner` al admin.
- Dar acceso del admin al store inicial.
- Suspender el usuario demo `owner@solidpos.local` después del bootstrap productivo.
- Registrar auditoría `production.bootstrap.completed`.
- Mantener el flujo protegido por `X-SolidPOS-Provision-Key`.

## Endpoints agregados

```http
GET  /api/v1/provisioning/status
POST /api/v1/provisioning/tenants/bootstrap
```

`GET /provisioning/status` no expone secretos; solo informa si el bootstrap está configurado/habilitado.

`POST /provisioning/tenants/bootstrap` requiere header:

```http
X-SolidPOS-Provision-Key: <PROVISION_KEY>
```

## Migración agregada

```text
database/postgresql/016_production_provisioning_bootstrap.sql
```

Crea:

```text
pos.production_bootstrap_runs
```

También registra el permiso documental:

```text
provisioning.bootstrap
```

El endpoint no usa JWT porque sirve para crear el primer admin real antes de que exista sesión productiva. La protección es por `PROVISION_KEY` y comparación en tiempo constante.

## Variables requeridas

En Railway/Supabase production debe existir:

```env
PROVISION_KEY=<clave larga aleatoria>
```

También puede usarse:

```env
Provisioning__BootstrapKey=<clave larga aleatoria>
```

## Scripts agregados

```text
scripts/provisioning/bootstrap-production-tenant.ps1
scripts/provisioning/verify-production-bootstrap.ps1
```

## Ejemplo de bootstrap remoto

```powershell
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

## Verificación del admin real

```powershell
.\scripts\provisioning\verify-production-bootstrap.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "<tenantId-del-bootstrap>" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```

## Criterios de aceptación

La iteración se considera PASS cuando:

```text
dotnet restore solidpos-platform.sln              PASS
dotnet build solidpos-platform.sln                PASS
dotnet test solidpos-platform.sln                 PASS
.\scripts\apply-postgresql-migrations.ps1         PASS
GET /api/v1/provisioning/status                   PASS
POST /api/v1/provisioning/tenants/bootstrap       PASS
POST /api/v1/auth/login admin real                PASS
GET /api/v1/tenants/current admin real            PASS
GitHub Actions                                    PASS
Railway deploy                                    PASS
```

## Decisión arquitectónica

El seed demo sigue existiendo para desarrollo, staging y smoke tests. Producción real debe usar bootstrap productivo y después suspender el usuario demo.

No se usa `owner@solidpos.local` como dependencia productiva.

## Riesgos controlados

- `PROVISION_KEY` debe rotarse si se expone.
- El endpoint de bootstrap debe usarse solo durante bootstrap inicial o bajo control operativo.
- El idempotency key debe ser único por tenant productivo.
- El admin real debe usar correo real y contraseña que cumpla la política.
