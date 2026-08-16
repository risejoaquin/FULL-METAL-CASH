# Macro Phase 02 Status - PostgreSQL + RLS

## Goal

Validate PostgreSQL migrations and make RLS testable before implementing auth, catalog, cash or sales modules.

## Implemented

- Expanded permission seeds.
- Added `pos.seed_mvp_roles(p_tenant_id uuid)`.
- Added PostgreSQL integration test helper.
- Added migration application test.
- Added RLS enabled metadata test.
- Added forced RLS tenant isolation test.
- Added tenant-scoped MVP role seed test.
- Updated README with DB test instructions.

## How To Run DB Tests

Use a disposable PostgreSQL database.

PowerShell:

```powershell
$env:SOLIDPOS_TEST_POSTGRES="Host=localhost;Port=5432;Database=solidpos_test;Username=solidpos;Password=solidpos_dev_password"
dotnet test tests/SolidPOS.PosServer.IntegrationTests/SolidPOS.PosServer.IntegrationTests.csproj
```

## Expected Result

- Integration tests pass.
- If `SOLIDPOS_TEST_POSTGRES` is not set, DB-specific tests return without running destructive schema reset.

## Safety

The tests run:

```sql
DROP SCHEMA IF EXISTS pos CASCADE;
```

Never point `SOLIDPOS_TEST_POSTGRES` at production or a valuable development database.
