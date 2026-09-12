# V1.1-04 Package Manifest

**Bloque:** `V1.1-04 - PostgreSQL Connection & Query Hardening`
**Baseline esperado:** `6bda38f6f922fdb619132650d957a6f7a79b6b3c`
**Tipo:** authorized patch artifact
**Migración:** `database/postgresql/020_postgresql_query_hardening.sql`
**schemaVersion:** 4 (sin cambio)
**syncContract:** `schema_version_4` (sin cambio)

## Archivos autorizados

- `.github/workflows/solidpos-ci.yml`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/PostgreSql/PostgreSqlConnectionStringResolver.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/Sales/PostgreSqlSalesRepository.cs`
- `src/PosServer/SolidPOS.PosServer.Contracts/Observability/OperationalMetricsResponse.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/PostgreSqlOperationalMetricsRepository.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/PrometheusMetricsFormatter.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/ProductionAlertEvaluator.cs`
- `tests/SolidPOS.PosServer.UnitTests/Observability/OperationalMetricsContractTests.cs`
- `tests/SolidPOS.PosServer.UnitTests/Observability/PrometheusMetricsFormatterTests.cs`
- `tests/SolidPOS.PosServer.UnitTests/Observability/ProductionAlertEvaluatorTests.cs`
- `tests/SolidPOS.PosServer.UnitTests/PostgreSqlConnectionHardeningTests.cs`
- `database/postgresql/020_postgresql_query_hardening.sql`
- `scripts/apply-postgresql-migrations.ps1`
- `scripts/v1.1/validate-v1.1-04-postgresql-query-hardening.ps1`
- `SOLIDPOS_V1_1_04_POSTGRESQL_CONNECTION_QUERY_HARDENING.md`
- `V1_1_04_PACKAGE_MANIFEST.md`
- `V1_1_04_VALIDATION_COMMANDS.md`

No se autoriza modificar otros archivos. Si el baseline no coincide, abortar sin aplicar.
