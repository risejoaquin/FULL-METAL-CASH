# SolidPOS GA-09 — Performance, Capacity, Resilience and Offline Readiness

## Estado de este paquete

Este paquete **inicia GA-09**. No declara PASS por sí solo.

GA-09 solo queda cerrada cuando el usuario ejecute el validator en producción y pegue logs reales que contengan:

```text
[GA-09] GA-09 PASS GA PERFORMANCE CAPACITY RESILIENCE OFFLINE READINESS / GO GA-10
```

## Base usada

Base del repo: `solidpos-platform-ga-08-hotfix-08-11-powershell-assertion-type-safety-20260823.zip`.

Punto de continuidad:

```text
GA-08 PASS GA SECURITY TENANT ISOLATION ACCESS CONTROL / GO GA-09
```

## Qué se cambió

Se agregaron artefactos de validación GA-09:

- `scripts/ga/validate-ga-09-performance-capacity-resilience-offline-readiness.ps1`
- `scripts/ga/ga-09-performance-capacity-resilience-offline-readiness-check.sql`
- `docs/ga/ga-09-performance-capacity-resilience-offline-readiness.md`
- `docs/ga/ga-09-evidence-matrix.md`
- `docs/ga/ga-09-go-no-go.md`
- `GA_09_VALIDATION_COMMANDS.md`

No se modificó backend/API, contratos C#, migraciones productivas ni dashboard.

## Módulos afectados

- `scripts/ga`: nuevo validator GA-09 y SQL de evidencia.
- `docs/ga`: contrato, matriz de evidencia y go/no-go GA-09.
- raíz del repo: comandos de validación e informe de implementación.

## Decisión técnica

GA-09 se implementa como **gate de validación productiva controlada**, no como migración destructiva ni cambio de backend.

El validator ejecuta:

1. Guardrails locales: restore/build/test/secret scan.
2. Revalidación opcional de GA-08 como prerequisite.
3. Login productivo sin imprimir tokens.
4. Health/readiness bajo carga controlada.
5. Endpoints protegidos bajo carga controlada.
6. Checks de resiliencia con errores controlados 400/401/403/404/409.
7. Snapshot SQL pre/post carga para RLS, sync, schema v4, duplicados financieros, locks y conexiones.
8. Manifest `.runtime` y log en `docs/ga/logs`.

## Riesgos

- La prueba genera tráfico real contra producción; por defecto el volumen es bajo y controlado.
- La latencia puede variar por Railway, red local, cold starts o pool PostgreSQL.
- Si falla por latencia/configuración de infraestructura, no implica hotfix de código automáticamente.
- Si falla por contrato/script, se corrige el validator.
- Si falla por defecto real de API/backend, se prepara hotfix completo.

## Criterios de PASS

GA-09 exige:

- `/health/live` y `/health/ready` 200.
- Endpoint protegido sin auth 401.
- Login productivo PASS.
- `/api/v1/sync/contract` mantiene `currentSchemaVersion = 4`.
- `syncContract = schema_version_4`.
- RLS sin tablas tenant-scoped faltantes.
- `legacySchemaEventCount = 0`.
- `retryOverSlaCount = 0`.
- Sin duplicados de ventas/pagos por IDs locales.
- Sin locks anómalos ni queries largas post-load.
- Error rate dentro de umbral configurado.
- p95/p99 dentro de umbrales configurados.
- `generalAvailabilityActivated = False`.

## Salida esperada

```text
[GA-09] GA-09 evidence manifest and performance snapshot PASS
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
