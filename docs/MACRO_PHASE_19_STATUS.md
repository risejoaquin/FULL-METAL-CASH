# Macro Fase 19: Soft Deletes + Audit Events Base

## Objetivo

Agregar archivado sin borrado fisico y auditoria formal para operaciones criticas de administracion, ventas, caja, inventario y sincronizacion.

## Implementado

| Area | Implementacion |
| --- | --- |
| Auditoria | `IAuditEventWriter` + `PostgreSqlAuditEventWriter` escriben en `pos.audit_events`. |
| Contexto | Cada evento guarda `tenant_id`, `actor_user_id`, `terminal_id`, IP, user-agent y trace id cuando existen. |
| Admin catalogo | Mutaciones admin escriben auditoria y siguen publicando deltas en `pos.sync_changes`. |
| Soft delete | `DELETE /api/v1/admin/catalog/{entityType}/{entityId}` actualiza `deleted_at`, no borra filas. |
| Sync delta | Soft delete publica delta `delete` en `tenant.catalog` o `price.updated` segun entidad. |
| Ventas | `sale.completed` y `sale.voided` auditan despues de persistir dominio. |
| Caja | `cash.shift.opened`, `cash.movement.created` y `cash.shift.closed` auditan despues de persistir dominio. |
| Inventario | `inventory.adjustment.created` audita despues de escribir ledger. |
| Sync | `sync.push.ingested` y `sync.process.completed` auditan lotes de outbox/inbox. |
| OpenAPI | Contrato actualizado con `AdminSoftDelete`. |
| Tests | Unit tests actualizados para inyeccion de auditoria y soft delete admin. |

## Entidades soportadas por soft delete

| `entityType` en URL | Tabla |
| --- | --- |
| `categories` | `pos.categories` |
| `products` | `pos.products` |
| `variants` | `pos.product_variants` |
| `barcodes` | `pos.product_barcodes` |
| `prices` | `pos.product_prices` |
| `modifier-groups` | `pos.modifier_groups` |
| `modifiers` | `pos.modifiers` |
| `recipes` | `pos.recipes` |

## Smoke test esperado

1. Crear una categoria temporal desde admin.
2. Capturar cursor con `GET /api/v1/sync/pull`.
3. Ejecutar soft delete de la categoria temporal.
4. Confirmar delta `tenant.catalog` con operacion `delete`.
5. Confirmar filas recientes en `pos.audit_events`.

## Nota de diseno

El soft delete no afecta tablas historicas de ventas, pagos, caja o ledger. Esas operaciones se compensan con estados o movimientos append-only, manteniendo trazabilidad y evitando perdida de evidencia contable.
