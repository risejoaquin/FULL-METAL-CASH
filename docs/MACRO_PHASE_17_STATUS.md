# Macro Fase 17: Admin Runtime Mutations Base

Estado: implementado.

## Objetivo

Cerrar el circuito nube -> POS local para cambios administrativos reales:

1. Admin modifica datos en PosServer.
2. PosServer persiste en PostgreSQL con tenant context.
3. PosServer publica un delta en `pos.sync_changes`.
4. La terminal descarga el delta por `GET /api/v1/sync/pull?cursor=...`.

## Endpoints agregados

| Endpoint | Permiso | Delta producido |
| --- | --- | --- |
| `PUT /api/v1/admin/catalog/categories/{categoryId}` | `catalog.manage` | `tenant.catalog` |
| `PUT /api/v1/admin/catalog/products/{productId}` | `catalog.manage` | `tenant.catalog` |
| `PUT /api/v1/admin/catalog/prices/{priceId}` | `catalog.manage` | `price.updated` |
| `PUT /api/v1/admin/access/users/{userId}` | `users.manage` | `tenant.access` |
| `PUT /api/v1/admin/access/roles/{roleId}/permissions` | `roles.manage` | `tenant.access` |

## Diseño

- Los endpoints viven bajo `/admin` para separarlos del runtime POS.
- `AdminMutationService` valida tenant context, reglas mínimas y publica cambios solo si la persistencia fue exitosa.
- `PostgreSqlAdminMutationRepository` concentra SQL explícito con RLS activo por `PostgreSqlTenantSession`.
- Categorías y productos usan optimistic concurrency opcional por `expectedVersion`.
- Cambios de usuarios, roles y permisos publican `tenant.access` para que la terminal refresque su snapshot de acceso.
- Cambios de catálogo publican `tenant.catalog`.
- Cambios de precio publican `price.updated`.

## Verificación esperada

1. Guardar cursor con `GET /api/v1/sync/pull`.
2. Ejecutar una mutación admin.
3. Consultar `GET /api/v1/sync/pull?cursor=...`.
4. Confirmar que el delta aparece en la respuesta.
5. Confirmar la fila en `pos.sync_changes`.

## Alcance pendiente

- CRUD completo de variantes, códigos de barras, modificadores y recetas.
- Creación de usuarios con contraseña/invitación.
- Versionado granular para precios si se decide agregar `version` a `product_prices`.
- Auditoría formal en `audit_events` para cada mutación admin.
