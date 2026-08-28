# Macro Phase 01 Status - PosServer Foundation

## Implemented

- `solidpos-platform` repository layout
- `SolidPOS` namespace convention
- `PosServer` projects separated by responsibility
- API bootstrap
- health endpoint
- readiness endpoint with PostgreSQL probe
- JSON structured logs
- correlation id middleware
- tenant/user/terminal log enrichment middleware
- OpenTelemetry console traces
- PostgreSQL migration scripts
- Docker Compose local environment
- base test projects
- README and logging guide

## Verification Limitation

The current execution environment does not have the `dotnet` CLI installed, so build/test execution must be performed locally by the user.

Expected local commands:

```bash
dotnet restore solidpos-platform.sln
dotnet build solidpos-platform.sln
dotnet test solidpos-platform.sln
```

## Next Macro Phase

Implement PostgreSQL migration validation, tenant context/RLS execution path, auth foundation and RBAC scaffolding.

## Fix Log

### 2026-08-15

Issue:

- Test projects restored, but build failed because test source files missed explicit `using Xunit;`.

Fix:

- Added `using Xunit;` to unit, integration and contract test files.

Note:

- Docker error reported by tester means Docker is not installed or not available in PATH on the tester machine. It is not a source-code failure.

## Macro Phase 02 Additions

Implemented after PASS from Macro Phase 01:

- PostgreSQL integration test helper.
- Migration application test.
- RLS metadata verification test.
- Forced RLS tenant isolation test for stores.
- Tenant-scoped MVP role seed function.
- Integration test for `seed_mvp_roles`.

Important:

- PostgreSQL integration tests require `SOLIDPOS_TEST_POSTGRES`.
- Use a disposable database because tests drop and recreate the `pos` schema.
