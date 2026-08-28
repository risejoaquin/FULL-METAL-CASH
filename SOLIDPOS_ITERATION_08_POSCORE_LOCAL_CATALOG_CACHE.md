# SolidPOS Iteration 08 — PosCore Local Catalog/Inventory Cache

## Estado

Pendiente de validación en máquina del usuario.

## Objetivo

Eliminar la dependencia de `ProductId` manual en scripts operativos de PosCore y empezar a operar como POS local real:

- sincronizar catálogo remoto desde PosServer hacia SQLite local;
- guardar productos, SKU, nombre, precio y moneda en cache local;
- vender offline usando SKU local cacheado;
- mantener snapshot de nombre/precio dentro del evento `sale.completed`;
- validar venta offline con datos cacheados hasta materializarla en PosServer.

## Alcance técnico

Esta iteración no agrega WPF ni UI táctil. Endurece el runtime local sobre el que WPF se montará después.

## Cambios principales

- Nueva tabla SQLite `local_catalog_products`.
- Nueva tabla SQLite `local_catalog_sync_state`.
- Nuevo modelo `LocalCatalogProduct`.
- Nuevo servicio `CatalogCacheService`.
- Nuevo cliente HTTP `HttpRemoteCatalogClient` contra `GET /api/v1/tenant/catalog`.
- Nuevo comando CLI `sync-catalog`.
- Nuevo comando CLI `catalog-status`.
- Nuevo comando CLI `sale-offline-from-cache`.
- Nuevo script E2E `scripts/poscore/validate-poscore-local-catalog-inventory-cache.ps1`.
- Nuevas pruebas unitarias de cache de catálogo.

## Contrato remoto usado

```http
GET /api/v1/tenant/catalog
Authorization: Bearer <terminal-access-token>
```

El terminal token ya incluye `catalog.read`, por eso PosCore puede sincronizar catálogo sin usar credenciales admin dentro del runtime local.

## Cierre esperado

```text
PosCore local catalog cache offline sale completed.
processedCount >= 1
saleDescription = Americano 12oz
saleUnitPriceCents = 4500
saleTotalCents = 4500
cashSalesCents = 4500
syncStatusDeadLetterCount = 0
deadLetterListCount = 0
```
