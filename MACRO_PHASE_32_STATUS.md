# Macro Fase 32 — Builder / Updates API

Estado: IMPLEMENTED — pending local validation
Fecha: 2026-08-16

## Objetivo

Cerrar la primera superficie API para PosBuilder y actualizaciones de PosCore:

- proyectos de builder
- branding por proyecto
- builds generados
- canales de actualización
- releases por tenant o globales
- metadata de versión
- check de actualización
- política explícita de no sobrescribir branding local

## Endpoints implementados

```http
GET  /api/v1/builder/projects
POST /api/v1/builder/projects
POST /api/v1/builder/projects/{projectId}/builds

GET  /api/v1/updates/channels
POST /api/v1/updates/releases
GET  /api/v1/updates/check
```

## Decisiones

1. PosBuilder no compila binarios reales todavía; esta fase registra metadata y deja el contrato runtime listo.
2. `builder_projects.branding` es snapshot de branding del proyecto, no configuración viva del tenant.
3. `updates/check` devuelve `brandingPolicy = preserve_local_branding` para que el updater no reemplace branding local durante update.
4. Canales soportados: `stable`, `beta`, `internal`.
5. Package type soportado: `velopack`.
6. Releases pueden ser tenant-scoped o globales.

## Archivos modificados/agregados

- `src/PosServer/SolidPOS.PosServer.Contracts/BuilderUpdates/BuilderUpdatesContracts.cs`
- `src/PosServer/SolidPOS.PosServer.Application/BuilderUpdates/IBuilderUpdatesRepository.cs`
- `src/PosServer/SolidPOS.PosServer.Application/BuilderUpdates/IBuilderUpdatesService.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/BuilderUpdates/BuilderUpdatesService.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/BuilderUpdates/PostgreSqlBuilderUpdatesRepository.cs`
- `src/PosServer/SolidPOS.PosServer.Api/Endpoints/BuilderUpdatesEndpoints.cs`
- `src/PosServer/SolidPOS.PosServer.Api/Program.cs`
- `database/postgresql/014_builder_updates_runtime.sql`
- `database/postgresql/003_seed_mvp_defaults.sql`
- `scripts/apply-postgresql-migrations.ps1`
- `contracts/openapi/solidpos-api-v1.openapi.yaml`
- `tests/SolidPOS.PosServer.UnitTests/BuilderUpdates/BuilderUpdatesContractTests.cs`

## Auditoría

```text
builder.project.created
builder.build.created
updates.release.created
```

## Estado esperado

```text
Build/test                              PENDIENTE
Migración 014                           PENDIENTE
GET /builder/projects                   PENDIENTE
POST /builder/projects                  PENDIENTE
POST /builder/projects/{id}/builds      PENDIENTE
GET /updates/channels                   PENDIENTE
POST /updates/releases                  PENDIENTE
GET /updates/check                      PENDIENTE
Audit builder/updates                   PENDIENTE
ContractTests OpenAPI parity            PENDIENTE
```
