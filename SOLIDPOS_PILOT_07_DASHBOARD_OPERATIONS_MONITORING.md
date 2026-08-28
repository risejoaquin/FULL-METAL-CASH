# SolidPOS PILOT-07 - Dashboard Operations Monitoring

## Estado

PENDING USER VALIDATION

## Objetivo

Validar monitoreo operativo real en PosDashboard contra PosServer production.

PILOT-07 cubre:

- build production de PosDashboard
- self-test dashboard
- cliente dashboard para `/api/v1/observability/metrics`
- health/readiness production
- login admin production
- operational metrics production
- database readiness monitor
- request/API monitor
- sync/dead-letter/conflict monitor
- sales latency monitor
- payments monitor
- inventory risk monitor
- audit trail monitor
- cross-check SQL contra PostgreSQL/Supabase
- GO/NO-GO

## Contratos usados

```text
GET  /health/live
GET  /health/ready
POST /api/v1/auth/login
GET  /api/v1/observability/metrics
GET  /api/v1/sync/status
GET  /api/v1/sync/dead-letter
GET  /api/v1/sync/conflicts
GET  /api/v1/audit/events
GET  /api/v1/sales
```

## Cambios

- Se extendio `PosServerClient` con `OperationalMetricsDto` y `getOperationalMetrics`.
- Se extendio `OperationsSnapshot` con `operationalMetrics`.
- Se amplio `OperationsDashboard` con secciones operativas reales:
  - Database monitor
  - API monitor
  - Conflict monitor
  - Sales latency
  - Payment monitor
  - Inventory risk
- Se amplio `scripts/self-test.mjs` para exigir `/api/v1/observability/metrics`.
- Se agrego validador PILOT-07.
- Se agrego SQL de cross-check productivo.

## Modulos afectados

```text
src/PosDashboard/SolidPOS.PosDashboard.Admin
scripts/pilot
docs/pilot
```

No se tocan backend, PosCore, migraciones ni seed productivo.

## Decision tecnica

PILOT-07 no crea datos productivos. Solo observa y cruza informacion operativa ya expuesta por PosServer.

El dashboard consume el endpoint de observabilidad ya existente y lo combina con endpoints operativos protegidos para construir un command center util para operacion diaria.

## Riesgos

- `failedRequests` puede aumentar si hay llamadas externas fallidas historicas del runtime.
- `deadLetterSync` puede ser mayor a cero por evidencias controladas de PILOT-06.
- La validacion no exige cero dead-letter porque PILOT-06 dejo un dead-letter controlado como evidencia.
- Requiere Node/npm local.
- Requiere Docker local para `postgres:16 psql`.

## GO / NO-GO

GO si:

- dashboard compila
- self-test pasa
- `/api/v1/observability/metrics` responde shape completo
- DB ready = true
- requiredTablesPresent = true
- sync metrics API cuadran con SQL
- sales/payments/inventory metrics API cuadran con SQL
- endpoints de dashboard responden con shape aceptado

NO-GO si:

- build dashboard falla
- observability metrics no existe o no esta protegido correctamente
- requiredTablesPresent = false
- SQL cross-check no cuadra fuera de tolerancia
- login/admin falla

## Hotfix 07.1

Validator hardening for `/api/v1/sync/conflicts` empty-list shape.

`status=pending` can legitimately return zero conflicts. The validator now checks that the endpoint response is non-null and normalizes items without requiring a non-empty result.


## Hotfix 07.2

- Corrige el orden de argumentos Docker/psql en `scripts/pilot/validate-dashboard-operations-monitoring.ps1`.
- Reemplaza `-tAc` por `-tA` al ejecutar SQL desde archivo con `-f`.
- Pasa `ON_ERROR_STOP=1` y `tenant_id` como variables reales de `psql` antes de `-f`.
- No toca backend, Dashboard, PosCore ni migraciones.
