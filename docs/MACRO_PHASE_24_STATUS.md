# Macro Fase 24 — Digital Receipts

Estado: IMPLEMENTED — pendiente validación local.

## Objetivo

Cerrar el recibo POS digital sin SAT/facturación fiscal:

- Persistencia en `digital_receipts`.
- `receipt_number` estable por venta.
- `public_token` con hash almacenado.
- `public_url` para consulta pública.
- Estado de emisión (`active`, `revoked`, `expired`) más `issued_at`.
- Reenvío/email stub auditable.
- Contrato OpenAPI.
- Tests unitarios de servicio.

## Endpoints agregados

- `POST /api/v1/receipts/{saleId}/issue`
- `GET /api/v1/receipts/{saleId}/digital`
- `GET /api/v1/receipts/public/{publicToken}`
- `POST /api/v1/receipts/{saleId}/email`

`GET /api/v1/receipts/{saleId}` permanece como read model no persistente del recibo.

## Decisión arquitectónica

El recibo normal y el recibo digital son conceptos separados:

- `GET /receipts/{saleId}` reconstruye el ticket desde la venta.
- `POST /receipts/{saleId}/issue` emite y persiste una referencia digital.
- `GET /receipts/{saleId}/digital` recupera la emisión persistida.
- `GET /receipts/public/{publicToken}` habilita visor por token.
- `POST /receipts/{saleId}/email` registra un reenvío stub sin integrar proveedor real.

## Persistencia

Se agregó `database/postgresql/008_digital_receipts_runtime.sql` para ampliar la tabla existente:

- `receipt_number`
- `issued_at`
- `last_sent_at`
- `last_sent_email`
- `send_count`
- `metadata`

La tabla sigue sin guardar el token público en claro como columna dedicada; se conserva `public_token_hash` y `public_url`.

## Auditoría

Acciones nuevas:

- `receipt.issued`
- `receipt.email_stub_queued`

## Pendiente de validación local

- Build/test.
- Migración `008` sobre DB local existente.
- Emisión digital de una venta real.
- Lectura protegida por saleId.
- Lectura pública por token.
- Email stub.
- Auditoría de emisión/reenvío.


## Hotfix 24.1

- Corrige compilación en `DigitalReceiptService.ExtractPublicToken`: usa `LastIndexOf("/", StringComparison.Ordinal)` en lugar de la sobrecarga inválida con `char` + `StringComparison`.
- No cambia contratos ni persistencia.

## Hotfix 24.2

- Corrige analyzer `xUnit2009` en `DigitalReceiptServiceTests`: reemplaza `Assert.True(result.PublicUrl.StartsWith(...))` por `Assert.StartsWith(...)`.
- No cambia lógica productiva, contratos, endpoints ni persistencia.

## Hotfix 24.3

- Endurece `GET /api/v1/audit/events` para validación de recibos digitales.
- Agrega compatibilidad con `limit` como alias de `pageSize` en la consulta de auditoría.
- Hace lectura defensiva de `before_data` / `after_data` y `occurred_at` para evitar que eventos históricos o serialización de JSON rompan el endpoint de auditoría con `500`.
- No cambia la persistencia ni los endpoints de recibo digital.

## Hotfix 24.4

Validated server logs showed `GET /api/v1/audit/events?limit=20` failed before reaching the audit service because Minimal API treated non-nullable query parameters `Page`, `PageSize`, and `Limit` as required. `AuditEventsQuery` now uses nullable query parameters and resolves defaults explicitly:

- `page` defaults to `1`.
- `pageSize` takes precedence when provided and positive.
- `limit` works as a compatibility alias when `pageSize` is not provided.
- missing pagination parameters no longer trigger `BadHttpRequestException`.

This hotfix only changes audit endpoint binding/defaulting. Digital receipt persistence, public token lookup, protected lookup, and email stub behavior remain unchanged.
