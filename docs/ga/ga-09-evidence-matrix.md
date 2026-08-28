# GA-09 — Evidence Matrix

| Evidencia | Fuente | Criterio |
|---|---|---|
| Build local | `dotnet build solidpos-platform.sln --no-restore` | ExitCode 0 |
| Tests locales | `dotnet test solidpos-platform.sln --no-build` | ExitCode 0 |
| Secret scan | `scripts/security/scan-local-secrets.ps1` | PASS |
| GA-08 prerequisite | `validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1` | PASS o evidencia externa si se usa `-SkipGa08Revalidation` |
| Health live | `/health/live` | 200 bajo carga |
| Health ready | `/health/ready` | 200 bajo carga |
| Auth negativo | `/api/v1/observability/metrics` sin token | 401 |
| Login | `/api/v1/auth/login` | accessToken y refreshToken no vacíos; no imprimir tokens |
| Sync contract | `/api/v1/sync/contract` | `currentSchemaVersion = 4` |
| Protected read load | sync/sales/reports | p95/p99 dentro de umbral; error rate permitido |
| Resilience negatives | filtros inválidos/missing sale/sync push sin runtime | 400/404/401/403/409 controlado |
| RLS | SQL GA-09 | missing RLS = 0; missing policies = 0 |
| Sync/offline backlog | SQL GA-09 | `retryOverSlaCount = 0`; `legacySchemaEventCount = 0` |
| Financial idempotency | SQL GA-09 | duplicate local sales/payments = 0 |
| PostgreSQL pressure | SQL GA-09 | waiting locks = 0; long running queries = 0 |
| GA activation | manifest GA-09 | `generalAvailabilityActivated = false` |


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
