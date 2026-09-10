# V1.1-02 Package Manifest

**Bloque:** `V1.1-02 — Production Metrics & Alerting`
**Baseline esperado:** `main@be9969aee6c65bd3683859d0cf604a7c3fa08b70`
**Tipo:** full-repo package
**Migraciones:** ninguna
**Schema:** 4 sin cambios
**Sync contract:** schema_version_4 sin cambios

## Archivos modificados

- `.github/workflows/solidpos-ci.yml`
- `src/PosServer/SolidPOS.PosServer.Api/Endpoints/ObservabilityEndpoints.cs`
- `src/PosServer/SolidPOS.PosServer.Api/appsettings.json`
- `src/PosServer/SolidPOS.PosServer.Application/Observability/IOperationalMetricsRepository.cs`
- `src/PosServer/SolidPOS.PosServer.Contracts/Observability/OperationalMetricsResponse.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/OperationalMetricsService.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/PostgreSqlOperationalMetricsRepository.cs`
- `tests/SolidPOS.PosServer.UnitTests/Observability/OperationalMetricsContractTests.cs`

## Archivos creados

- `deploy/observability/solidpos-v1.1-alert-rules.yml`
- `scripts/v1.1/validate-v1.1-02-production-metrics-alerting.ps1`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/ProductionAlertEvaluator.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/PrometheusMetricsFormatter.cs`
- `tests/SolidPOS.PosServer.UnitTests/Observability/ProductionAlertEvaluatorTests.cs`
- `tests/SolidPOS.PosServer.UnitTests/Observability/PrometheusMetricsFormatterTests.cs`
- `SOLIDPOS_V1_1_02_PRODUCTION_METRICS_ALERTING.md`
- `V1_1_02_VALIDATION_COMMANDS.md`
- `V1_1_02_PACKAGE_MANIFEST.md`

## Archivos eliminados

Ninguno.
