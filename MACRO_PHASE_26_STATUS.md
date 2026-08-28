# Macro Fase 26 — Customers API

Estado: IMPLEMENTED — pending local validation.

## Objetivo

Cerrar el modulo operativo de clientes e historial comercial despues de ventas, recibos digitales y devoluciones.

## Alcance implementado

- `GET /api/v1/customers`
- `POST /api/v1/customers`
- `GET /api/v1/customers/{customerId}`
- `PATCH /api/v1/customers/{customerId}`
- `GET /api/v1/customers/{customerId}/sales`
- Cliente opcional en venta mediante `customerId` existente en `CreateSaleRequest`.
- Historial de compras por cliente.
- Gasto bruto, refunds, gasto neto, ultimo ticket y ticket promedio.
- Auditoria `customer.created` y `customer.updated`.
- Permisos `customers.read` y `customers.manage`.
- Migracion runtime `010_customers_runtime.sql` con indices para busqueda/historial.
- Contrato OpenAPI actualizado.
- Tests de contratos de Customers.

## Decisiones

- Las ventas sin cliente siguen siendo validas: `customerId` permanece opcional.
- El historial excluye ventas `voided` y usa ventas `completed`, `partially_returned` y `returned`.
- El gasto neto se calcula como `totalCents - refundCents`.
- `customers.manage` controla crear y actualizar clientes.
- `customers.read` controla consulta de clientes e historial.
- Los tokens de terminal incluyen permisos de customer read/manage para soportar alta rapida de cliente desde POS.

## Validaciones esperadas

- Crear cliente.
- Listar cliente por busqueda.
- Consultar cliente por id.
- Actualizar telefono/status/limite.
- Crear venta asociada a `customerId`.
- Consultar historial del cliente.
- Validar `salesCount`, `grossSalesCents`, `refundCents`, `netSpentCents`, `averageTicketCents` y `lastPurchaseAt`.
- Validar auditoria `customer.created` / `customer.updated`.
