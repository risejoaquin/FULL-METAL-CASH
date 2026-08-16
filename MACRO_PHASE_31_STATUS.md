# Macro Fase 31 — Admin/Tenant Management Completion

Status: IMPLEMENTED — pending local validation

## Objetivo

Cerrar administración backend antes de seguir creciendo:

- Tenant current context.
- Tenant/POS settings.
- Stores CRUD básico.
- Users CRUD básico.
- Roles/permissions read model.
- Auditoría administrativa.
- OpenAPI parity obligatorio por Fase 30.

## Endpoints agregados

```http
GET   /api/v1/tenants/current
PATCH /api/v1/tenants/current/settings
GET   /api/v1/stores
POST  /api/v1/stores
PATCH /api/v1/stores/{storeId}
GET   /api/v1/users
POST  /api/v1/users
PATCH /api/v1/users/{userId}
GET   /api/v1/roles
GET   /api/v1/permissions
```

## Seguridad

Permisos usados:

- `tenant.manage`
- `stores.manage`
- `users.manage`
- `roles.manage`

Owner mantiene todos los permisos. Admin recibe `tenant.manage` en seed MVP para poder administrar configuraciones base.

## Auditoría

Acciones nuevas:

- `tenant.settings.updated`
- `store.created`
- `store.updated`
- `user.created`
- `user.updated`

## Decisiones

- Roles/permissions se exponen como read model administrativo.
- La modificación granular de permisos por rol sigue viviendo en `/admin/access/roles/{roleId}/permissions`.
- Usuarios pueden recibir roles por `roleIds` o `roleCodes`.
- Usuarios pueden recibir acceso por tienda con `storeIds`.
- La contraseña solo se expone en requests, nunca en responses.

## Archivos principales

- `src/PosServer/SolidPOS.PosServer.Api/Endpoints/AdminManagementEndpoints.cs`
- `src/PosServer/SolidPOS.PosServer.Application/AdminManagement/*`
- `src/PosServer/SolidPOS.PosServer.Contracts/AdminManagement/AdminManagementContracts.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/AdminManagement/*`
- `contracts/openapi/solidpos-api-v1.openapi.yaml`
- `database/postgresql/003_seed_mvp_defaults.sql`
- `tests/SolidPOS.PosServer.UnitTests/AdminManagement/AdminManagementContractTests.cs`

## Validación esperada

```text
Build/test                              PENDIENTE
GET /tenants/current                    PENDIENTE
PATCH /tenants/current/settings         PENDIENTE
GET /stores                             PENDIENTE
POST /stores                            PENDIENTE
PATCH /stores/{storeId}                 PENDIENTE
GET /roles                              PENDIENTE
GET /permissions                        PENDIENTE
GET /users                              PENDIENTE
POST /users                             PENDIENTE
PATCH /users/{userId}                   PENDIENTE
Auditoría tenant/store/user             PENDIENTE
ContractTests OpenAPI parity            PENDIENTE
```
