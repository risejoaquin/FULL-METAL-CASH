# GA-09 — Performance, Capacity, Resilience and Offline Readiness

## Propósito

Validar que SolidPOS puede operar bajo condiciones realistas de producción sin degradar seguridad, tenant isolation, integridad financiera, RLS, schema v4 ni sync/offline.

## Alcance validado por el gate

### API performance

- `/health/live`
- `/health/ready`
- `/api/v1/auth/login`
- `/api/v1/sync/status`
- `/api/v1/sync/contract`
- `/api/v1/sales`
- `/api/v1/reports/dashboard/overview`
- `/api/v1/observability/metrics`

### Métricas capturadas

- total requests
- successful requests
- failed requests
- error percent
- p50 latency
- p95 latency
- p99 latency
- max latency

### PostgreSQL / RLS / sync

El SQL de GA-09 captura:

- tablas tenant-scoped con RLS;
- tablas tenant-scoped sin RLS;
- tablas tenant-scoped sin policy;
- buckets de `sync_inbox_events`;
- `retryOverSlaCount`;
- `legacySchemaEventCount`;
- conflictos pendientes/resueltos;
- conexiones activas;
- locks no concedidos;
- queries activas largas;
- índices críticos para sales/sync.

### Integridad financiera

El gate valida que después de la carga controlada no existan:

- duplicados de `sales` por `(tenant_id, terminal_id, local_sale_id)`;
- duplicados de `payments` por `(tenant_id, sale_id, local_payment_id)`.

## Reglas

- No activar General Availability.
- No avanzar a GA-10 sin logs reales.
- No imprimir tokens ni secretos.
- No cambiar `schemaVersion=4`.
- No cambiar `syncContract=schema_version_4`.
- No crear migraciones productivas para este gate salvo evidencia posterior de defecto real.

## Cierre esperado

```text
[GA-09] GA-09 PASS GA PERFORMANCE CAPACITY RESILIENCE OFFLINE READINESS / GO GA-10
```


## GA-09.4 capacity boundary condition

GA-09 production evidence from 2026-08-24 established a bounded PASS profile:

- `Concurrency 1`: PASS, 0% errors, p95/p99 inside threshold.
- `Concurrency 2`: PASS, 0% errors, p95/p99 inside threshold.
- `Concurrency 3+`: current Railway/upstream path returns intermittent `400 upstream error` and must not be treated as public launch capacity.

This condition does not prove a backend functional defect, schema drift, RLS regression, sync corruption, or financial-data issue. It is carried into GA-10 as an observability/alerting requirement and into GA-12 as a launch-readiness capacity decision.

```text
GA-09: PASS REAL PRODUCTION / GO GA-10
Known condition: Railway/upstream capacity boundary at Concurrency 3+
GA public launch: NOT activated
```
