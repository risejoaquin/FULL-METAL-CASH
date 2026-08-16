# Macro Fase 16: Sync Change Producer Base

Estado: implementado.

## Objetivo

El servidor ahora produce cambios append-only en `pos.sync_changes` cuando ocurren mutaciones cloud-side que el POS local debe descargar por `GET /api/v1/sync/pull`.

## Cambios incluidos

- `ISyncChangeWriter` como puerto de aplicación para publicar deltas.
- `PostgreSqlSyncChangeWriter` como adaptador PostgreSQL hacia `pos.sync_changes`.
- Producción automática de delta `tenant.config` cuando se actualiza `PUT /api/v1/tenant/config`.
- Producción automática de delta `terminal.updated` cuando una terminal se registra o se revoca.
- Registro DI del writer en PosServer API.
- Pruebas unitarias para validar que config y terminales escriben cambios de sync.

## Entidades producidas en esta fase

| Evento de servidor | `entity_type` | `operation` | Alcance |
| --- | --- | --- | --- |
| Actualización de POS Builder config | `tenant.config` | `update` | Tenant completo |
| Registro/revinculación de terminal | `terminal.updated` | `update` | Store de la terminal |
| Revocación de terminal | `terminal.updated` | `update` | Tenant completo |

## Siguiente expansión natural

Cuando existan endpoints administrativos de catálogo, precios, usuarios, roles y permisos, deben usar el mismo `ISyncChangeWriter` con estas entidades:

| Dominio | `entity_type` sugerido |
| --- | --- |
| Categorías/productos/variantes/códigos de barras | `tenant.catalog` o entidad granular |
| Precios | `price.updated` |
| Usuarios | `tenant.access` |
| Roles/permisos | `tenant.access` |

## Verificación esperada

1. Hacer un `sync/pull` inicial y guardar `nextCursor`.
2. Actualizar `tenant/config` con un usuario admin/owner.
3. Hacer `sync/pull` usando el cursor guardado.
4. El delta debe incluir `entityType = tenant.config` con `operation = update`.
5. La tabla `pos.sync_changes` debe mostrar filas nuevas.
