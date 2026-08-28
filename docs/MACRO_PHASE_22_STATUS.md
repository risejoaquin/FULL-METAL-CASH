# Macro Fase 22: Report Accuracy Hardening + Dashboard Read Models

## Objetivo

Endurecer la exactitud de reportes operativos, hacer que `storeId` sea un filtro validado contra el tenant, exponer trazabilidad de consumo BOM/modificadores y entregar un read model consolidado listo para un dashboard React.

## Implementado

### 1. Filtro `storeId` endurecido

- Todo reporte que recibe `storeId` valida primero que la sucursal exista dentro del tenant autenticado.
- Un UUID de otra organización deja de producir silenciosamente un reporte vacío y se rechaza como filtro inválido.
- El tenant continúa obteniéndose exclusivamente de `ITenantContext`; nunca se acepta `tenantId` desde query string.

### 2. Semántica financiera de reportes corregida

`SalesRangeReportResponse` separa ahora:

- `GrossSalesCents`: subtotal antes de descuentos.
- `DiscountCents`: descuentos de ventas completadas.
- `NetSalesCents`: subtotal menos descuentos, sin impuestos ni propinas.
- `TaxCents`: impuestos.
- `TipCents`: propinas.
- `TotalSalesCents`: total final cobrado por la venta, incluyendo impuestos y propinas.
- `AverageTicketCents`: promedio de `total_cents` de ventas completadas.

El reporte por método de pago descuenta el cambio de los métodos `cash`. Cuando existen varios métodos cash en una misma venta, el cambio se asigna proporcionalmente al efectivo entregado para conservar la reconciliación total.

### 3. Tiempo de negocio consistente en inventario

`GET /api/v1/reports/inventory/movements` filtra y ordena por `local_occurred_at`, no por `created_at` del servidor. Esto evita distorsionar el rango cuando una venta offline llega posteriormente mediante sincronización.

El contrato incorpora trazabilidad:

- `effect`
- `recipeId`
- `modifierId`
- `modifierBehavior`

### 4. Dashboard read model

Nuevo endpoint:

`GET /api/v1/reports/dashboard/overview`

Filtros:

- `storeId`
- `from`
- `to`
- `limit` (`20` default, `200` máximo)
- `trendBucket=hour|day`

Si `trendBucket` no se envía:

- rango de hasta 72 horas -> `hour`
- rango mayor -> `day`

El read model contiene:

- resumen financiero de ventas
- ventas por método de pago
- top productos
- inventario negativo
- turnos de caja recientes
- resumen de movimientos BOM/modificadores
- serie temporal de ventas para gráficas React

## Corrección de BOM/modificadores: sustitución de leche

Se agregó la migración `007_modifier_inventory_semantics.sql`.

Los modificadores soportan ahora:

- `inventory_behavior = none`
- `inventory_behavior = add`
- `inventory_behavior = substitute`
- `consumption_quantity`
- `consumption_unit_id`
- `replaces_product_id`
- `replaces_variant_id`

La semántica se incluye en:

- PostgreSQL
- contratos Admin
- catálogo runtime
- snapshot de línea de venta
- cálculo de inventario
- OpenAPI

### Latte demo

La opción normal `Leche entera` queda con `inventory_behavior = none`, porque la receta base ya consume la leche entera.

La opción `Leche de avena` queda configurada como:

- `inventory_behavior = substitute`
- `linked_product_id = ING-AVENA-ML`
- `consumption_quantity = 250`
- `consumption_unit_id = ml`
- `replaces_product_id = ING-LECHE-ML`

Para un Latte x1 con leche de avena el ledger esperado es:

- `ING-CAFE-G = -18`
- `ING-AVENA-ML = -250`
- `ING-VASO-12 = -1`
- no debe existir movimiento de `ING-LECHE-ML`

El desperdicio configurado en la receta también se aplica al ingrediente añadido o sustituto para mantener la misma base de consumo del BOM.

## Pruebas agregadas/endurecidas

- validación de `storeId` ajeno al tenant en `ReportsServiceTests`
- normalización del dashboard y bucket de tendencia
- verificación de columnas de semántica de modificadores en migraciones
- integración `Latte_with_oat_milk_replaces_base_milk_instead_of_double_consuming`
- integración de reconciliación de efectivo: venta $65.00 pagada con $100.00 reporta $65.00 netos por método

Las pruebas PostgreSQL continúan dependiendo de `SOLIDPOS_TEST_POSTGRES` y se ejecutan localmente por el mantenedor del proyecto.

## Migraciones

Orden actualizado:

1. `001_initial_schema_postgresql.sql`
2. `002_seed_permissions.sql`
3. `003_seed_mvp_defaults.sql`
4. `005_sync_push_runtime.sql`
5. `006_sync_processing_runtime.sql`
6. `007_modifier_inventory_semantics.sql`

La migración 007 se aplica también sobre un schema existente cuando se usa `apply-postgresql-migrations.ps1` sin `-ResetSchema`.

## Definition of Done de Macro Fase 22

- [x] `storeId` validado contra tenant.
- [x] semántica de `NetSales` y `TotalSales` separada.
- [x] cambio de efectivo descontado de reportes por método de pago.
- [x] movimientos de inventario por tiempo de negocio (`local_occurred_at`).
- [x] trazabilidad BOM/modificadores visible en reportes.
- [x] read model consolidado para dashboard React.
- [x] serie temporal hour/day para gráficas.
- [x] sustitución de leche modelada explícitamente.
- [x] Latte con avena configurado a 250 ml y sin doble descuento de leche base.
- [x] OpenAPI actualizado.
- [x] pruebas unitarias/integración preparadas para ejecución local.


## Hotfix 22.1 - Local DB replay hardening

- Re-running `007_modifier_inventory_semantics.sql` now corrects existing development databases where the legacy base milk modifier had already been backfilled as `inventory_behavior = 'add'` with `consumption_quantity = 1`.
- The base `Leche entera` modifier is explicitly forced to `inventory_behavior = 'none'`.
- The `Leche de avena` modifier is explicitly forced to `inventory_behavior = 'substitute'`, `consumption_quantity = 250`, `consumption_unit_id = ml`, and `replaces_product_id = ING-LECHE-ML`.
- Report endpoints now parse optional `storeId` manually so invalid UUID values return a controlled `400 Invalid report filter` instead of surfacing as an internal server error.

Existing historical inventory ledger rows are not rewritten by the hotfix. Validate the milk correction with a new sale after re-applying migrations/seed and restarting the API.

## Hotfix 22.2 - Explicit report query binding

Fecha: 2026-08-16.

Motivo: en ejecución local se confirmó que `GET /api/v1/reports/dashboard/overview` con `storeId=REEMPLAZA_CON_STORE_ID_VALIDO` todavía podía regresar `500` porque el Minimal API binder intentaba convertir el query parameter a `Guid?` antes de llegar a la validación manual del endpoint.

Cambios:

- `ReportsEndpoints` deja de usar `[AsParameters]` para los endpoints de reportes.
- `storeId` se declara explícitamente como `string?` en cada endpoint de reportes.
- La conversión a `Guid?` queda centralizada en `TryParseOptionalGuid`.
- Un `storeId` inválido debe responder `400 Invalid report filter`, no `500`.
- Un `storeId` válido pero no perteneciente al tenant sigue siendo rechazado por `ReportsService.StoreFilterIsValidAsync`.

Nota de validación de leche/modificadores:

- Los movimientos históricos no se reescriben. Si en la base local ya existían ventas antes de la corrección, seguirán apareciendo movimientos legacy como `modifier_linked_ingredient` y `ING-LECHE-ML -1` dentro del rango consultado.
- La validación correcta debe hacerse creando una venta nueva después de aplicar migraciones y seed de esta versión.
- Para inspección directa en ambientes Docker, usar `docker exec -i solidpos-postgres psql -U solidpos -d solidpos` en lugar de `psql` local si PostgreSQL CLI no está instalado en Windows.


## Final validation note — PASS

Macro Fase 22 queda cerrada como PASS antes de iniciar Macro Fase 23.

Validación manual confirmada:

- `storeId` inválido en dashboard devuelve `400`.
- `storeId` válido devuelve `200`.
- `Leche entera` queda como `inventory_behavior = none`.
- `Leche de avena` queda como `inventory_behavior = substitute`, `consumption_quantity = 250`, y reemplaza `ING-LECHE-ML`.
- Venta nueva de Latte con leche de avena descuenta `ING-CAFE-G -18`, `ING-VASO-12 -1`, `ING-AVENA-ML -250` y no descuenta `ING-LECHE-ML`.
- Dashboard reporta `modifierSubstituteMovementCount = 1` para la venta nueva.
