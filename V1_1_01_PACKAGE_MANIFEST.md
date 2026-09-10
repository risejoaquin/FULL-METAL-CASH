# V1.1-01 Package Manifest

## Package
`solidpos-platform-v1.1-01-observability-foundation-ci-20260909.zip`

## Baseline
SolidPOS v1.0 QSR — Public GA active / post-v1 roadmap baseline.

## Contract invariants
- `schemaVersion = 4` unchanged.
- `syncContract = schema_version_4` unchanged.
- no PostgreSQL migration.
- no SQLite migration.
- no functional API contract removal/change.
- Public GA rollout state unchanged.
- inventory modifier semantics unchanged.

## Modified / added files
- `.github/workflows/solidpos-ci.yml` (new)
- `src/PosServer/SolidPOS.PosServer.Api/Program.cs`
- `src/PosServer/SolidPOS.PosServer.Api/appsettings.json`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/CorrelationIdMiddleware.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/OperationalMetricsMiddleware.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/RequestLogEnrichmentMiddleware.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/SolidPosTelemetry.cs` (new)
- `src/PosServer/SolidPOS.PosServer.Infrastructure/Sync/SyncEventProcessingService.cs`
- `tests/SolidPOS.PosServer.UnitTests/Observability/CorrelationIdMiddlewareTests.cs` (new)
- `scripts/v1.1/validate-v1.1-01-observability-foundation.ps1` (new)
- `SOLIDPOS_V1_1_01_OBSERVABILITY_FOUNDATION.md` (new)
- `V1_1_01_VALIDATION_COMMANDS.md` (new)
- `V1_1_01_PACKAGE_MANIFEST.md` (new)

## Validation status at package generation
Static source/package validation completed in the generation environment. The generation environment does not contain the .NET SDK, therefore build/test are intentionally **not declared PASS** here. GitHub Actions now provides the primary automated evidence for secret scan/restore/build/test; local PowerShell evidence remains required for the runtime-specific V1.1-01 validator and any diagnostic failure.
