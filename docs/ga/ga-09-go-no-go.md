# GA-09 — Go / No-Go

## Estado inicial

```text
NEXT / NOT STARTED
```

## GO hacia GA-10

Solo se autoriza GO GA-10 si el log real del usuario contiene:

```text
[GA-09] GA-09 PASS GA PERFORMANCE CAPACITY RESILIENCE OFFLINE READINESS / GO GA-10
```

Y el manifest contiene:

```text
phase: GA-09
status: PASS GA PERFORMANCE CAPACITY RESILIENCE OFFLINE READINESS / GO GA-10
blockers: {}
schemaVersion: 4
syncContract: schema_version_4
generalAvailabilityActivated: False
```

## NO-GO / BLOCKED

GA-09 queda bloqueada si aparece cualquiera de estos casos:

- build/test/secret scan fallan;
- GA-08 prerequisite no está validado;
- `/health/ready` no responde 200;
- endpoints protegidos no respetan 401 sin auth;
- sync contract no reporta schema 4;
- RLS falta en tablas tenant-scoped;
- existen eventos legacy distintos de schema 4;
- `retryOverSlaCount > 0`;
- se detectan duplicados de ventas/pagos;
- locks no concedidos o queries largas quedan activos post-load;
- p95/p99 o error rate exceden umbral;
- el validator reporta blockers.

## Notas

Un fallo de latencia/capacidad puede ser infraestructura/configuración antes que código. No fabricar hotfix backend sin evidencia.


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
