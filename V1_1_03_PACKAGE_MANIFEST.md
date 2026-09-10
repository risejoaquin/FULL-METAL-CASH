# V1.1-03 Package Manifest

**Bloque:** `V1.1-03 - Advanced Health / Readiness Diagnostics`
**Baseline esperado:** `main@09ea409f9c94b8fd793df2fd2aced47cfaec4e1a`
**Tipo:** full-repo package
**Migraciones:** ninguna
**schemaVersion = 4**
**syncContract = schema_version_4**

## Cambios

- amplía el contrato de `ReadinessResponse`;
- endurece `PostgreSqlReadinessProbe` con breakdown, latency y schema/sync compatibility;
- actualiza OpenAPI para el contrato aditivo;
- agrega pruebas unitarias de invariantes de readiness;
- agrega validator V1.1-03;
- agrega gate estático V1.1-03 a GitHub Actions.

## No cambia

- sales/payments/receipts;
- inventario/modificadores;
- RLS/RBAC;
- schema físico PostgreSQL;
- sync event contract;
- deployment topology.

## Archivos modificados

- `.github/workflows/solidpos-ci.yml`
- `contracts/openapi/solidpos-api-v1.openapi.yaml`
- `src/PosServer/SolidPOS.PosServer.Api/Program.cs`
- `src/PosServer/SolidPOS.PosServer.Contracts/System/ReadinessResponse.cs`
- `src/PosServer/SolidPOS.PosServer.Infrastructure/PostgreSql/PostgreSqlReadinessProbe.cs`

## Archivos creados

- `SOLIDPOS_V1_1_03_ADVANCED_HEALTH_READINESS_DIAGNOSTICS.md`
- `V1_1_03_PACKAGE_MANIFEST.md`
- `V1_1_03_VALIDATION_COMMANDS.md`
- `scripts/v1.1/validate-v1.1-03-advanced-health-readiness.ps1`
- `tests/SolidPOS.PosServer.UnitTests/PostgreSqlReadinessProbeTests.cs`
