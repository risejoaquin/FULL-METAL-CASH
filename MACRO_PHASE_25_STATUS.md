# Macro Fase 25 — Returns / Refunds

Estado: IMPLEMENTED — pending local validation.

Base: Macro Fase 24 Hotfix 24.4.

## Alcance

Se implementa devolución POS sin SAT/facturación:

- `POST /api/v1/returns`
- `GET /api/v1/returns/{returnId}`
- `GET /api/v1/returns?saleId=&from=&to=&limit=`

## Reglas cerradas

- Solo se devuelve contra una venta existente del mismo tenant/store/terminal.
- La venta debe estar `completed` o `partially_returned`.
- La terminal debe tener el cash shift original abierto.
- La devolución puede ser total o parcial.
- No se permite devolver más cantidad de la vendida por línea.
- `refundCents` debe coincidir con el total devuelto.
- El refund se registra en `return_refunds`.
- Si el refund es cash, se reduce `cash_shifts.expected_cash_cents`.
- El inventario se reintegra usando movimientos positivos tipo `return` basados en los movimientos originales de venta.
- La venta queda `partially_returned` o `returned` según cantidad devuelta.
- Se audita `return.created`.

## Cambios de DB

Nueva migración:

- `database/postgresql/009_returns_refunds_runtime.sql`

Extiende:

- `returns.cash_shift_id`
- `returns.status`
- `returns.refund_cents`
- `returns.metadata`

Agrega:

- `return_refunds`
- índices de returns/listado/refunds

## Reportes/dashboard

`SalesRangeReportResponse` ahora expone:

- `returnCount`
- `refundCents`
- `netAfterReturnsCents`

Los reportes de ventas consideran ventas `completed`, `partially_returned` y `returned` como ventas históricas para lectura bruta, y el neto después de devoluciones se expone por separado.
