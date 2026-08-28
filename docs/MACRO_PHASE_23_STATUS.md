# Macro Fase 23 — Sales Retrieval + Receipt Read Models

Status: IMPLEMENTED — pending local validation

## Objetivo

Cerrar la lectura operativa de ventas ya realizadas para que POS, ERP React y futuras fases de recibos/devoluciones puedan reconstruir una venta completa después de haberla creado.

## Alcance implementado

- `GET /api/v1/sales/{saleId}`
  - Lee una venta tenant-scoped por id.
  - Incluye cabecera, líneas, modificadores expandidos, pagos y movimientos de inventario asociados.
  - Incluye movimientos `sale` y `sale_void` para reconstrucción/auditoría operativa.

- `GET /api/v1/sales?from=&to=&storeId=&terminalId=&status=&limit=`
  - Lista ventas para historial POS/ERP.
  - Filtros por rango, tienda, terminal, estatus y límite.
  - `storeId` y `terminalId` inválidos devuelven `400` antes de tocar servicio.
  - UUIDs válidos pero ajenos al tenant son rechazados por repositorio con `409`.

- `GET /api/v1/receipts/{saleId}`
  - Devuelve un read model de recibo/ticket no fiscal.
  - Incluye tenant/store/terminal/cajero, líneas, modificadores, pagos, totales, efectivo pagado y cambio.
  - No envía email ni crea recibo público todavía; eso queda para Macro Fase 24.

## Contratos nuevos

- `SaleListItemResponse`
- `SaleDetailResponse`
- `SaleDetailLineResponse`
- `SaleModifierResponse`
- `SaleInventoryMovementResponse`
- `ReceiptResponse`
- `ReceiptLineResponse`
- `ReceiptModifierResponse`
- `ReceiptPaymentResponse`

## Seguridad y permisos

Se agregó el permiso:

- `sales.read`

Roles actualizados:

- `owner`: todos los permisos.
- `admin`: `sales.read`.
- `manager`: `sales.read`.
- `cashier`: `sales.read`.

## Decisiones arquitectónicas

- `CreateSale` conserva `SaleResponse` compacto para no alterar el contrato de escritura ya probado.
- `GET /sales/{saleId}` usa `SaleDetailResponse`, no `SaleResponse`, porque la lectura operativa necesita modifiers expandidos e inventario asociado.
- El recibo usa un read model separado (`ReceiptResponse`) para no mezclar auditoría operativa con formato de ticket.
- Los modifiers del detalle/recibo se reconstruyen desde el snapshot guardado en `sale_lines.snapshot`, no desde catálogo vivo. Esto evita que cambios posteriores al catálogo modifiquen ventas históricas.
- Los movimientos de inventario se leen desde ledger append-only por `reference_id = saleId`, preservando trazabilidad.

## Macro Fase 22 registrada como cerrada

La semántica de inventario para modificadores `none | add | substitute` queda registrada como cerrada y validada antes de iniciar esta fase:

- `Leche entera`: `none`.
- `Leche de avena`: `substitute`.
- `Leche de avena`: consume `250 ml`.
- `Leche de avena`: reemplaza `ING-LECHE-ML`.
- Venta nueva validada: no descuenta `ING-LECHE-ML`; descuenta `ING-AVENA-ML -250` con `modifierBehavior = substitute`.

## Pruebas agregadas

- Unit tests de `SalesService` para:
  - lectura por id con tenant context;
  - rechazo de filtro inválido;
  - lectura de recibo con tenant context.

- Contract test de modelos de venta detallada:
  - venta con modifiers expandidos;
  - movimiento de inventario con `modifierBehavior = substitute`.

- Integration test PostgreSQL:
  - crea venta con leche de avena;
  - valida `GET by repository` con modifiers e inventario;
  - valida listado filtrado;
  - valida recibo.

## Fuera de alcance

- Envío de recibo por email.
- Generación de URL pública de recibo.
- Devoluciones/refunds.
- Contract parity completa runtime vs OpenAPI.
- Paginación formal con cursor/meta; esta fase usa `limit` operativo.

## Hotfix 23.1 — Legacy sale list cash shift safety

Status: IMPLEMENTED — pending local validation.

Reason:
- Local development databases can contain historical completed sales created before cash-shift enforcement or before the current seed/runtime flow.
- The `GET /api/v1/sales` list read model assumed `sales.cash_shift_id` was always non-null and used `reader.GetGuid(4)` directly.
- If one historical sale has `cash_shift_id = NULL`, the whole list endpoint can fail with HTTP 500 before the user can select a sale.

Change:
- `SaleListItemResponse.CashShiftId` is now nullable (`Guid?`).
- `PostgreSqlSalesRepository.ListAsync` now reads `cash_shift_id` null-safely.

Scope:
- This does not weaken creation/void invariants.
- New completed sales still require an open cash shift.
- The change only makes the read model tolerant of legacy/local historical rows.

Validation target:
- `GET /api/v1/sales?from=&to=&storeId=&status=completed&limit=20` must return 200 instead of 500.
- If old rows exist without `cash_shift_id`, they should return `cashShiftId: null` instead of crashing the endpoint.

## Hotfix 23.2 — Sales list query hardening

Status: IMPLEMENTED — pending local validation.

The sales listing read model no longer aggregates `sale_lines` and `payments` through joined `GROUP BY`. It now uses correlated count subqueries for `lineCount` and `paymentCount`, which avoids runtime failures caused by PostgreSQL grouping rules or legacy rows while preserving the same response contract.

Validation target:

- `GET /api/v1/sales?from=&to=&storeId=&status=completed&limit=20` must return `200 OK`.
- Rows with nullable `cash_shift_id` remain readable for development history.
- Invalid filters continue returning `400`.

## Hotfix 23.3 — Sales list reader disposal before commit

Status: IMPLEMENTED — pending local validation.

Reason:
- Local validation showed `NpgsqlOperationInProgressException: A command is already in progress` when `GET /api/v1/sales` tried to commit the transaction while the list `NpgsqlDataReader` was still active.
- The query itself was correct after Hotfix 23.2; the runtime failure was caused by reader lifetime, not SQL shape.

Change:
- `PostgreSqlSalesRepository.ListAsync` now wraps the sales list `NpgsqlDataReader` in an explicit `await using (...) { ... }` block.
- The reader is fully disposed before `transaction.CommitAsync(...)` is called.

Validation target:
- `GET /api/v1/sales?from=&to=&storeId=&status=completed&limit=20` must return `200 OK`.
- Server logs must no longer show `NpgsqlOperationInProgressException` for the sales list endpoint.
