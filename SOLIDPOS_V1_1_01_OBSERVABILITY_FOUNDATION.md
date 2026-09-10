# SolidPOS v1.1-01 — Observability Foundation

## Estado de implementación
Implementado sobre el baseline SolidPOS v1.0 QSR Public GA. Este bloque no cambia esquema PostgreSQL/SQLite, contratos API funcionales, `schemaVersion=4`, `syncContract=schema_version_4`, semántica de inventario ni rollout de producción.

## Alcance implementado
- OpenTelemetry tracing para ASP.NET Core y HttpClient.
- Suscripción al `ActivitySource` de Npgsql para tracing PostgreSQL.
- OpenTelemetry metrics con export local por consola.
- `Meter` propio `SolidPOS.PosServer` para request count, errores, latencia y resultados de procesamiento sync.
- Correlation ID y Request ID sanitizados y devueltos en headers.
- `traceId`, `correlationId` y `requestId` en ProblemDetails de errores no manejados.
- Contexto diagnóstico tenant-safe: no se agregan IDs crudos de tenant/user/store/terminal a logs/traces del middleware de observabilidad.
- Clasificación de excepciones no manejadas mediante `error.type` y estado de Activity.
- Conservación del endpoint tenant-scoped existente `/api/v1/observability/metrics` para métricas operativas DB/sync/sales/payments/inventory/audit.
- Validator PowerShell específico de fase.
- CI automática en GitHub Actions sobre `windows-latest` para secret scan, restore, build y tests completos.

## Decisiones técnicas
1. No se añade Prometheus en V1.1-01; export/alerting pertenece a V1.1-02.
2. PostgreSQL se traza mediante la instrumentación `ActivitySource` expuesta por Npgsql, sin envolver ni alterar repositorios de negocio.
3. Se evita cardinalidad y exposición de datos eliminando IDs crudos de identidad de la telemetría transversal. La correlación end-to-end usa trace/correlation/request IDs.
4. Las métricas de request usan rutas normalizadas del endpoint y no URLs completas con IDs.
5. No hay migraciones en esta fase.

## Riesgos
- El exporter de consola es apropiado para validación/local, no sustituye backend de observabilidad de producción.
- La telemetría Npgsql depende del `ActivitySource` provisto por Npgsql 8; el validator/build debe confirmar compatibilidad de paquetes.
- El endpoint operacional existente sigue consultando DB al solicitar métricas; V1.1-03/V1.1-04 podrán endurecer diagnóstico/costo sin cambiar este contrato silenciosamente.

## Criterios PASS
- restore/build/test PASS.
- correlation/request IDs presentes y correlacionables.
- OpenTelemetry tracing/metrics configurados.
- Npgsql tracing habilitado.
- no IDs crudos de tenant/user/store/terminal en el middleware diagnóstico transversal.
- health/live y health/ready PASS.
- `/api/v1/observability/metrics` PASS con token autorizado.
- GitHub Actions `SolidPOS CI` PASS para secret scan/restore/build/test.
- sin cambios de schema/sync contract.
