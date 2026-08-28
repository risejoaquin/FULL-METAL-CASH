# GA-10 — Observability, Dashboard, Alerting and On-call Readiness

## Estado inicial

```text
NEXT / NOT STARTED
```

## Objetivo

Validar que SolidPOS puede operar después de GA-09 con señales observables suficientes para detectar degradación antes de activar General Availability pública.

## Condición heredada desde GA-09

GA-09 cerró como PASS real en producción con capacidad validada hasta `Concurrency 2`. Desde `Concurrency 3+` se detectaron errores `400 upstream error` en la ruta Railway/proxy/upstream.

Esta condición no demuestra defecto funcional del backend, pero sí obliga a GA-10 a validar alertas y monitoreo para:

- `400 upstream error`;
- timeouts;
- error rate;
- p95/p99;
- `health/ready`;
- dashboard/read-model endpoints;
- sync endpoints;
- presión de DB/conexiones;
- escalamiento on-call.

## Alcance

GA-10 valida:

1. endpoint protegido `/api/v1/observability/metrics`;
2. protección 401 sin token;
3. health live/ready;
4. dashboard operativo local o condición documentada si se usa `-SkipDashboardBuild`;
5. contrato de alertas y umbrales;
6. runbook on-call;
7. matriz de evidencia;
8. DB snapshot de audit/sync/financial/RLS/presión;
9. continuidad de `schemaVersion=4` y `syncContract=schema_version_4`;
10. no activación pública de GA.

## Resultado requerido

```text
[GA-10] GA-10 PASS GA OBSERVABILITY DASHBOARD ALERTING ONCALL READINESS / GO GA-11
```
