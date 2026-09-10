# SolidPOS v1.1-02 — Production Metrics & Alerting

**Producto:** SolidPOS
**Empresa:** SolidBit
**Baseline de entrada:** `main@be9969aee6c65bd3683859d0cf604a7c3fa08b70`
**Estado de v1.0:** CLOSED / Public GA ACTIVE
**schemaVersion:** `4`
**syncContract:** `schema_version_4`

## Objetivo

Cerrar el bloque oficial `V1.1-02 — Production Metrics & Alerting` agregando señales operativas verificables y contratos de alerta sin modificar contratos de negocio, esquema PostgreSQL/SQLite, sync v4, RLS, RBAC, idempotencia ni thresholds Public GA.

## Implementación

### Export Prometheus seguro

Se agrega:

`GET /api/v1/observability/prometheus`

El endpoint hereda `reports.read` del grupo de observabilidad. No se publica un `/metrics` anónimo. La salida usa Prometheus text exposition y no incluye `tenant_id`, user IDs, store IDs, terminal IDs, secretos ni PII.

Métricas principales:

- `solidpos_http_requests_total`
- `solidpos_http_failed_requests_total`
- `solidpos_http_error_ratio`
- `solidpos_http_p95_latency_ms`
- `solidpos_postgresql_active_connections`
- `solidpos_postgresql_active_non_client_wait_event`
- `solidpos_postgresql_client_read_wait_event`
- `solidpos_sync_pending`
- `solidpos_sync_processing`
- `solidpos_sync_retry_pending`
- `solidpos_sync_dead_letter`
- `solidpos_inventory_negative_stock`
- `solidpos_financial_sale_payment_mismatch`

### Semántica PostgreSQL

La señal blocker de presión PostgreSQL es exclusivamente:

`active_non_client_wait_event`

Se calcula sobre sesiones `state = 'active'`, con `wait_event IS NOT NULL` y `wait_event_type <> 'Client'`.

`ClientRead` se publica únicamente como diagnóstico independiente y nunca se usa como blocker.

### Financial integrity

Se agrega `SalePaymentMismatchCount` al read model de observabilidad. Para ventas `completed`, `partially_returned` o `returned`, compara `sales.paid_cents` contra la suma de pagos `approved`.

La consulta se ejecuta bajo el tenant context existente y RLS.

### Alert evaluation

Se agrega:

`GET /api/v1/observability/alerts`

Alertas:

- HTTP error ratio > 5% después de un mínimo de 20 requests observados.
- HTTP p95 > 1200 ms.
- `active_non_client_wait_event > 0`.
- sync `received + processing + retry_pending > 0`.
- deadletter > 1.
- negative stock > 0.
- sale/payment mismatch > 0.

Los thresholds de integridad y capacidad conservan los invariantes Public GA. `ClientRead` no participa en la decisión.

### Alert rules

Contrato declarativo:

`deploy/observability/solidpos-v1.1-alert-rules.yml`

Este archivo permite trasladar las mismas reglas a un Prometheus/Alertmanager administrado sin redefinir su semántica.

## Backwards compatibility

Cambios JSON en `/api/v1/observability/metrics` son únicamente aditivos:

- `database.activeNonClientWaitEventCount`
- `database.clientReadWaitEventCount`
- `financialIntegrity.salePaymentMismatchCount`

No se eliminan rutas ni campos previos.

No hay migraciones.

## Riesgos y mitigación

- **Falso blocker PostgreSQL por ClientRead:** mitigado separando `wait_event_type = Client` de waits activos de servidor.
- **Fuga de tenant/PII por Prometheus:** endpoint protegido y salida sin identificadores tenant/user/store/terminal.
- **Alertas por ruido de bajo volumen HTTP:** error ratio requiere mínimo de requests antes de activarse.
- **Regresión financiera:** mismatch usa la misma semántica de reconciliación usada en gates GA.
- **Cambio silencioso de thresholds:** thresholds quedan configurados y cubiertos por validator/tests.

## PASS

V1.1-02 puede declararse PASS solamente con evidencia de:

1. GitHub Actions completo PASS.
2. secret scan PASS.
3. restore/build/test PASS.
4. validator estático PASS.
5. tras promoción/deploy autorizado: `/health/live` = 200/alive.
6. `/health/ready` = 200/ready.
7. `/api/v1/observability/prometheus` = 200 con credencial válida y todas las métricas requeridas.
8. salida Prometheus sin `tenant_id`.
9. `/api/v1/observability/alerts` = 200 y contrato DB basado en `active_non_client_wait_event`.
10. `ClientRead` no es blocker.
11. schemaVersion 4 y syncContract schema_version_4 preservados.

No avanzar a V1.1-03 hasta revisión de evidencia.
