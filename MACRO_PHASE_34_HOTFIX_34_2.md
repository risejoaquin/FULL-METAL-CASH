# Macro Fase 34 — Hotfix 34.2

## Nombre
Production Deployment Hardening + PostgreSQL DATABASE_URL Compatibility

## Motivo
Railway construía y arrancaba el contenedor correctamente, pero `/health/ready` fallaba en producción. Después de corregir `AllowedHosts`, el endpoint pasó de `400` a `500`, lo que indicaba una excepción no controlada dentro del readiness.

## Decisión arquitectónica
El backend debe aceptar tanto:

- `ConnectionStrings__Postgres` en formato Npgsql: `Host=...;Port=...;Database=...;Username=...;Password=...`
- `DATABASE_URL` en formato URI: `postgresql://user:password@host:port/db?sslmode=require`

El endpoint `/health/ready` no debe devolver `500` por errores esperados de configuración, DB o migraciones. Debe devolver:

- `200` cuando PostgreSQL y tablas runtime están listas.
- `503` con diagnóstico JSON cuando falta conexión, formato válido, conexión real o tablas requeridas.

## Cambios incluidos

- Agregado `PostgreSqlConnectionStringResolver`.
- `/health/ready` ahora devuelve `ReadinessResponse` diagnosticable.
- Normalización automática de `DATABASE_URL` a connection string Npgsql.
- Validación de producción endurecida para `AllowedHosts`, `Cors`, JWT y PostgreSQL.
- `004_seed_dev_auth.sql` corregido para usar `public.crypt` y `public.gen_salt`.
- Agregado `scripts/ci/apply-production-migrations.sh`.
- Agregado job `production-migration` en GitHub Actions antes de `railway-deploy`.
- Actualizado OpenAPI para documentar `503` de readiness con `ReadinessResponse`.

## Archivos principales modificados

- `src/PosServer/SolidPOS.PosServer.Infrastructure/PostgreSql/PostgreSqlConnectionStringResolver.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/PostgreSql/PostgreSqlReadinessProbe.cs`
- `src/PosServer/SolidPOS.PosServer.Contracts/System/ReadinessResponse.cs`
- `src/PosServer/SolidPOS.PosServer.Api/Program.cs`
- `database/postgresql/004_seed_dev_auth.sql`
- `scripts/ci/apply-production-migrations.sh`
- `.github/workflows/posserver-ci-cd.yml`
- `scripts/validate-deployment-env.sh`
- `scripts/validate-deployment-env.ps1`
- `contracts/openapi/solidpos-api-v1.openapi.yaml`
- `deploy/railway/env.production.example`
- `deploy/railway/README.md`

## Regla de despliegue firme

1. CI pasa restore/build/test/contract/docker.
2. CI valida migraciones en DB temporal.
3. CI ejecuta `production-migration` contra `PRODUCTION_DATABASE_URL`.
4. Railway despliega.
5. Railway exige `/health/ready = 200`.
6. Smoke test valida API real.

## Estado
Implementado. Pendiente de validación local y Railway.
