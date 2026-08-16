# Macro Fase 33 — Observability + Production Hardening

Estado: IMPLEMENTED — pending local validation

## Objetivo

Endurecer PosServer antes de despliegue fuerte:

- DB metrics
- request metrics
- sync metrics
- sales latency
- failed payments
- negative inventory
- audit trails
- health checks reales
- readiness PostgreSQL
- structured errors
- rate limits
- CORS real
- AllowedHosts real
- Forwarded headers
- Railway hardening

## Implementado

### Endpoints

```http
GET /health/live
GET /health/ready
GET /api/v1/observability/metrics
```

`/health/ready` ahora valida conexión PostgreSQL y tablas runtime críticas.

`/api/v1/observability/metrics` devuelve señales operativas por tenant:

- database readiness
- active DB connections
- missing required tables
- request totals/failures/latency
- top routes
- sync inbox by status
- pending/resolved conflicts
- retry/dead-letter counters
- sales last 24h
- average sales persistence latency
- failed/declined payments
- negative inventory count
- low stock count
- audit events last 24h
- last audit event timestamp

### Production hardening

- `ForwardedHeaders` habilitado para proxy/reverse proxy/Railway.
- CORS configurado por `Cors:AllowedOrigins`.
- Producción rechaza configuración insegura si `AllowedHosts='*'`.
- Producción exige `Cors:AllowedOrigins` explícito.
- Rate limiting global por tenant o IP.
- ProblemDetails enriquecido con `traceId`, `correlationId` y `service`.
- Métricas de request en memoria con promedio y p95 por ruta.

## Decisiones

### Métricas runtime vs métricas DB

Se separan métricas en dos fuentes:

1. Runtime memory recorder:
   - request count
   - failed request count
   - average latency
   - p95 latency
   - top routes

2. PostgreSQL metrics/read models:
   - sales latency persisted
   - sync status
   - failed payments
   - negative inventory
   - audit trail health
   - DB readiness/tables

### Seguridad de producción

En Development se permite CORS local para pruebas. En Production se exige configuración explícita para evitar despliegues abiertos por accidente.

### Railway

Se habilitan forwarded headers para respetar `X-Forwarded-For`, `X-Forwarded-Proto` y `X-Forwarded-Host` detrás del proxy de Railway.

## Archivos principales

```text
src/PosServer/SolidPOS.PosServer.Api/Endpoints/ObservabilityEndpoints.cs
src/PosServer/SolidPOS.PosServer.Api/Program.cs
src/PosServer/SolidPOS.PosServer.Api/appsettings.json
src/PosServer/SolidPOS.PosServer.Api/appsettings.Development.json
src/PosServer/SolidPOS.PosServer.Contracts/Observability/OperationalMetricsResponse.cs
src/PosServer/SolidPOS.PosServer.Application/Observability/IOperationalMetricsService.cs
src/PosServer/SolidPOS.PosServer.Application/Observability/IOperationalMetricsRepository.cs
src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/OperationalMetricsRecorder.cs
src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/OperationalMetricsMiddleware.cs
src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/OperationalMetricsService.cs
src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/PostgreSqlOperationalMetricsRepository.cs
src/PosServer/SolidPOS.PosServer.Infrastructure/PostgreSql/PostgreSqlReadinessProbe.cs
contracts/openapi/solidpos-api-v1.openapi.yaml
tests/SolidPOS.PosServer.UnitTests/Observability/OperationalMetricsRecorderTests.cs
tests/SolidPOS.PosServer.UnitTests/Observability/OperationalMetricsContractTests.cs
.env.example
```

## Validación pendiente

```text
Build/test                              PENDIENTE
GET /health/live                        PENDIENTE
GET /health/ready                       PENDIENTE
GET /api/v1/observability/metrics       PENDIENTE
ProblemDetails trace/correlation         PENDIENTE
Rate limit config                       PENDIENTE
CORS config                             PENDIENTE
Forwarded headers config                PENDIENTE
ContractTests OpenAPI parity            PENDIENTE
```
