# SolidPOS Iteration 09 — PosCore Local Inventory Consumption Cache

## Objetivo

Que PosCore deje de vender únicamente desde catálogo cacheado y empiece a consumir inventario local estimado usando recetas/BOM cacheadas desde PosServer.

## Alcance entregado

- Cache local de recetas activas desde `/api/v1/tenant/catalog`.
- Cache local de recipe items.
- Tablas SQLite para recetas, items y movimientos locales de inventario.
- Venta offline desde SKU cacheado con consumo local de inventario.
- Movimientos locales `sale_recipe_component` generados antes del push remoto.
- Push/process remoto de `sale.completed`.
- Consulta de `pos.inventory_ledger` remoto por `remoteSaleId`.
- Reconciliación local/remota por cantidad de movimientos.
- Script E2E de validación.

## Nuevas tablas SQLite

- `local_inventory_recipes`
- `local_inventory_recipe_items`
- `local_inventory_cache_sync_state`
- `local_inventory_movements`

## Nuevos comandos PosCore CLI

- `sync-inventory-cache`
- `inventory-status`
- `sale-offline-from-cache-with-inventory`
- `inventory-reconcile`

## Criterio PASS

- Build limpio.
- Tests limpios.
- Smoke remoto PASS.
- Catálogo local cacheado.
- Recetas/BOM locales cacheadas.
- Venta offline desde SKU local.
- Movimiento local de inventario generado.
- Sync push/process remoto PASS.
- Venta remota materializada.
- `pos.inventory_ledger` remoto tiene movimientos para la venta.
- Reconciliación local/remota PASS.
- Dead-letter remoto = 0.
