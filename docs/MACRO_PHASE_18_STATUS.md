# Macro Fase 18: Admin Catalog Complete Base

Estado: implementado.

## Objetivo

Completar las mutaciones administrativas base del catalogo que el POS local necesita para operar QSR/offline-first:

- Variantes.
- Codigos de barras.
- Grupos de modificadores.
- Modificadores.
- Recetas/BOM.
- Ingredientes de recetas/BOM.

Todas las mutaciones exitosas publican `tenant.catalog` en `pos.sync_changes`.

## Endpoints agregados

| Endpoint | Permiso | Delta producido |
| --- | --- | --- |
| `PUT /api/v1/admin/catalog/variants/{variantId}` | `catalog.manage` | `tenant.catalog` |
| `PUT /api/v1/admin/catalog/barcodes/{barcodeId}` | `catalog.manage` | `tenant.catalog` |
| `PUT /api/v1/admin/catalog/modifier-groups/{modifierGroupId}` | `catalog.manage` | `tenant.catalog` |
| `PUT /api/v1/admin/catalog/modifiers/{modifierId}` | `catalog.manage` | `tenant.catalog` |
| `PUT /api/v1/admin/catalog/recipes/{recipeId}` | `catalog.manage` | `tenant.catalog` |
| `PUT /api/v1/admin/catalog/recipes/{recipeId}/items/{recipeItemId}` | `catalog.manage` | `tenant.catalog` |

## Reglas base

- Variantes requieren SKU, nombre y status valido.
- Codigos de barras requieren barcode y cantidad positiva.
- Grupos de modificadores requieren seleccion minima/maxima coherente.
- Modificadores requieren nombre.
- Recetas requieren version positiva, rendimiento positivo, merma no negativa y status valido.
- Ingredientes de receta requieren cantidad positiva.
- Los cambios se persisten con RLS activo por tenant context.
- Si la mutacion falla, no se publica delta.

## Observabilidad

Cada mutacion registra logs con `tenant_id`, entidad y version/delta cuando aplica. El pull posterior permite confirmar el cambio por `entityType = tenant.catalog`.

## Siguiente expansion natural

- Deletes suaves admin para catalogo completo.
- Auditoria formal `audit_events`.
- Recalculo de snapshot/version de catalogo por lote.
- Validaciones cruzadas mas estrictas para variantes/barcodes y modificadores vinculados.
