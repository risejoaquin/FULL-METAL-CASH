# HOTFIX GA-01.2 — Failed Requests Validation Window Semantics

## Motivo

Durante la revalidación fresca de BETA-10 desde GA-01, EXP-05 falló con `failed_requests` aunque health/readiness, DB, build y tests estaban PASS.

La causa es que `OperationalMetricsRecorder.FailedRequests` es un contador acumulativo en memoria durante toda la vida del proceso y cuenta cualquier respuesta HTTP >= 400. Un error histórico mantiene el contador mayor a cero hasta reiniciar el backend, por lo que `failedRequests > 0` no representa necesariamente un fallo actual.

## Corrección

`validate-exp-05-operational-monitoring-hardening.ps1` ahora:

- conserva `failedRequestsBaseline` como evidencia histórica;
- toma una segunda muestra al final de la ventana de probes;
- calcula `failedRequestsDelta`;
- bloquea solo si `failedRequestsDelta > 0`;
- mantiene `historical_failed_requests_process_lifetime` como condición cuando el baseline acumulado ya era > 0;
- no reinicia ni limpia métricas de producción.

## Seguridad

No se modifica:

- schemaVersion 4;
- syncContract schema_version_4;
- datos productivos;
- inventario;
- tenant isolation;
- API business contracts.

La evidencia histórica no se oculta ni elimina.
