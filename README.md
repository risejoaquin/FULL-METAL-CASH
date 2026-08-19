# SolidPOS Platform

SolidPOS MVP starts with `PosServer + PostgreSQL`.

This package contains the first backend macro phase:

- Clean/Hexagonal project layout
- ASP.NET Core Web API
- PostgreSQL migration scripts
- Serilog JSON logs
- correlation id middleware
- tenant/user/terminal log enrichment
- health and readiness endpoints
- JWT authentication base
- refresh-token rotation base
- RBAC permission policies
- authenticated tenant context
- OpenAPI contract copy
- Docker local environment
- base unit, integration and contract tests

## Requirements

- .NET SDK 8.0 or newer
- Docker Desktop
- PostgreSQL client tools if running migrations with `psql`

## Project Layout

```text
src/PosServer/
  SolidPOS.PosServer.Api/
  SolidPOS.PosServer.Application/
  SolidPOS.PosServer.Contracts/
  SolidPOS.PosServer.Domain/
  SolidPOS.PosServer.Infrastructure/
tests/
  SolidPOS.PosServer.UnitTests/
  SolidPOS.PosServer.IntegrationTests/
  SolidPOS.PosServer.ContractTests/
database/postgresql/
contracts/openapi/
deploy/
scripts/
```

## Run Locally With Docker

Docker is optional for this macro phase. If `docker` is not recognized on Windows, install Docker Desktop or skip this section and use the `dotnet` commands below.

```bash
docker compose up --build
```

API:

- `http://localhost:8080/health`
- `http://localhost:8080/health/ready`
- `http://localhost:8080/api/v1/system/info`
- `http://localhost:8080/api/v1/auth/login`
- `http://localhost:8080/api/v1/auth/refresh`
- `http://localhost:8080/api/v1/auth/logout`
- `http://localhost:8080/api/v1/auth/terminal/register`
- `http://localhost:8080/api/v1/terminals/enrollment-token`
- `http://localhost:8080/api/v1/terminals`
- `http://localhost:8080/api/v1/terminal/session`
- `http://localhost:8080/api/v1/tenant/config`
- `http://localhost:8080/api/v1/tenant/catalog`
- `http://localhost:8080/api/v1/inventory/stock`
- `http://localhost:8080/api/v1/sync/push`
- `http://localhost:8080/api/v1/sync/process`
- `http://localhost:8080/api/v1/sync/pull`

Swagger is enabled in Development:

- `http://localhost:8080/swagger`

## Apply PostgreSQL Migrations

If using the included Docker PostgreSQL:

```bash
export DATABASE_URL="postgresql://solidpos:solidpos_dev_password@localhost:5432/solidpos"
bash scripts/apply-postgresql-migrations.sh
```

Optional development auth seed:

```bash
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f database/postgresql/004_seed_dev_auth.sql
```

PowerShell on Windows:

```powershell
.\scripts\apply-postgresql-migrations.ps1
.\scripts\apply-dev-auth-seed.ps1
```

The PowerShell scripts use `psql` when available. If `psql` is not installed, they fall back to the `solidpos-postgres` Docker container from `docker-compose.yml`.

If the local `pos` schema already exists, the migration script skips `001_initial_schema_postgresql.sql` and reapplies idempotent seeds only. To rebuild the local development schema from zero:

```powershell
.\scripts\apply-postgresql-migrations.ps1 -ResetSchema
.\scripts\apply-dev-auth-seed.ps1
```

Demo login:

```json
{
  "email": "owner@solidpos.local",
  "password": "Admin123!",
  "tenantId": "11111111-1111-1111-1111-111111111111"
}
```

On PowerShell:

```powershell
$env:DATABASE_URL="postgresql://solidpos:solidpos_dev_password@localhost:5432/solidpos"
.\scripts\apply-postgresql-migrations.ps1
.\scripts\apply-dev-auth-seed.ps1
```

## Run Without Docker

```bash
dotnet restore solidpos-platform.sln
dotnet build solidpos-platform.sln
dotnet test solidpos-platform.sln
dotnet run --project src/PosServer/SolidPOS.PosServer.Api/SolidPOS.PosServer.Api.csproj
```

If PostgreSQL is not running, `/health` should still work and `/health/ready` should return a degraded/not-ready response.

PowerShell development runner:

```powershell
.\scripts\run-posserver-dev.ps1
```

This sets:

- `ASPNETCORE_ENVIRONMENT=Development`
- `ASPNETCORE_URLS=http://localhost:5000`
- `ConnectionStrings__Postgres=Host=localhost;Port=5432;Database=solidpos;Username=solidpos;Password=solidpos_dev_password`
- `Jwt__SigningKey=dev-only-solidpos-signing-key-change-before-production`

## Manual Auth And Terminal Smoke Test

Run PosServer in one PowerShell window:

```powershell
.\scripts\run-posserver-dev.ps1
```

Use another PowerShell window:

```powershell
$loginBody = @{
  email = "owner@solidpos.local"
  password = "Admin123!"
  tenantId = "11111111-1111-1111-1111-111111111111"
} | ConvertTo-Json

$ownerSession = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/auth/login" `
  -ContentType "application/json" `
  -Body $loginBody

$enrollmentBody = @{
  storeId = "22222222-2222-2222-2222-222222222222"
  expiresInMinutes = 60
} | ConvertTo-Json

$enrollment = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/terminals/enrollment-token" `
  -Headers @{ Authorization = "Bearer $($ownerSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $enrollmentBody

$registerBody = @{
  enrollmentToken = $enrollment.enrollmentToken
  name = "Caja 01"
  fingerprint = "DEV-TERMINAL-001"
  appVersion = "0.1.0-dev"
} | ConvertTo-Json

$terminalSession = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/auth/terminal/register" `
  -ContentType "application/json" `
  -Body $registerBody

$terminals = Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:5000/api/v1/terminals" `
  -Headers @{ Authorization = "Bearer $($ownerSession.accessToken)" }

$terminals

$terminalRuntime = Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:5000/api/v1/terminal/session" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" }

$terminalRuntime

$tenantConfigFromTerminal = Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:5000/api/v1/tenant/config" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" }

$tenantConfigFromTerminal

$catalog = Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:5000/api/v1/tenant/catalog" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" }

$catalog.products.Count
$catalog.variants.Count
$catalog.prices.Count
$catalog.modifierGroups.Count
$catalog.recipes.Count

try {
  Invoke-RestMethod `
    -Method Get `
    -Uri "http://localhost:5000/api/v1/cash-drawers/shifts/current" `
    -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" }
} catch {
  $_.Exception.Response.StatusCode.value__
}

$openShiftBody = @{
  openedByUserId = "33333333-3333-3333-3333-333333333333"
  openingAmountCents = 100000
} | ConvertTo-Json

$cashShift = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/cash-drawers/shifts" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $openShiftBody

$cashShift

$cashMovementBody = @{
  movementType = "cash_out"
  amountCents = 15000
  reason = "Pago proveedor hielo"
  createdByUserId = "33333333-3333-3333-3333-333333333333"
} | ConvertTo-Json

$cashMovement = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/cash-drawers/shifts/$($cashShift.id)/movements" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $cashMovementBody

$cashMovement

$saleBody = @{
  localSaleId = [guid]::NewGuid()
  cashierUserId = "33333333-3333-3333-3333-333333333333"
  occurredAt = (Get-Date).ToUniversalTime().ToString("o")
  localCreatedAt = (Get-Date).ToUniversalTime().ToString("o")
  lines = @(
    @{
      productId = "30000000-0000-0000-0000-000000000001"
      quantity = "1"
      discountCents = 0
      modifierIds = @("51000000-0000-0000-0000-000000000001")
    }
  )
  payments = @(
    @{
      localPaymentId = [guid]::NewGuid()
      methodCode = "cash"
      amountCents = 6500
    }
  )
  tipCents = 0
} | ConvertTo-Json -Depth 10

$sale = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/sales" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $saleBody

$sale

$sameSaleAgain = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/sales" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $saleBody

$sameSaleAgain.id -eq $sale.id

$voidSaleBody = @{
  voidedByUserId = "33333333-3333-3333-3333-333333333333"
  reason = "Error de captura demo"
  occurredAt = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json

$voidedSale = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/sales/$($sale.id)/void" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $voidSaleBody

$voidedSale.status

$sameVoidAgain = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/sales/$($sale.id)/void" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $voidSaleBody

$sameVoidAgain.id -eq $voidedSale.id

$stock = Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:5000/api/v1/inventory/stock" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" }

$stock | Select-Object productId,variantId,unitId,quantityOnHand

$stock | Where-Object {
  $_.productId -in @(
    "30000000-0000-0000-0000-000000000004",
    "30000000-0000-0000-0000-000000000005",
    "30000000-0000-0000-0000-000000000007"
  )
} | Select-Object productId,unitId,quantityOnHand

$adjustmentBody = @{
  localAdjustmentId = [guid]::NewGuid()
  storeId = "22222222-2222-2222-2222-222222222222"
  adjustmentType = "stock_in"
  reason = "Correccion manual demo"
  createdByUserId = "33333333-3333-3333-3333-333333333333"
  occurredAt = (Get-Date).ToUniversalTime().ToString("o")
  lines = @(
    @{
      productId = "30000000-0000-0000-0000-000000000004"
      quantityDelta = "18"
      unitId = "11000000-0000-0000-0000-000000000002"
    },
    @{
      productId = "30000000-0000-0000-0000-000000000005"
      quantityDelta = "251"
      unitId = "11000000-0000-0000-0000-000000000003"
    },
    @{
      productId = "30000000-0000-0000-0000-000000000007"
      quantityDelta = "1"
      unitId = "11000000-0000-0000-0000-000000000001"
    }
  )
} | ConvertTo-Json -Depth 10

$adjustment = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/inventory/adjustments" `
  -Headers @{ Authorization = "Bearer $($ownerSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $adjustmentBody

$adjustment

$sameAdjustmentAgain = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/inventory/adjustments" `
  -Headers @{ Authorization = "Bearer $($ownerSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $adjustmentBody

$sameAdjustmentAgain.id -eq $adjustment.id

$stockAfterAdjustment = Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:5000/api/v1/inventory/stock" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" }

$stockAfterAdjustment | Select-Object productId,variantId,unitId,quantityOnHand

$closeShiftBody = @{
  closedByUserId = "33333333-3333-3333-3333-333333333333"
  countedCashCents = 91500
} | ConvertTo-Json

$closedShift = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/cash-drawers/shifts/$($cashShift.id)/close" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $closeShiftBody

$closedShift

$updateConfigBody = @{
  businessVertical = "qsr_cafe"
  uiLayout = "touch_grid"
  modulesEnabled = @{
    modifiers = $true
    recipes_bom = $true
    barcode = $true
    cash = $true
    sync = $true
  }
  branding = @{
    name = "SolidPOS Demo Cafe"
    primaryColor = "#111827"
  }
  receiptSettings = @{
    paperWidth = "mm80"
    showLogo = $false
  }
  hardwareProfile = @{
    printer = "escpos_usb"
    cashDrawer = $true
    scanner = "keyboard_wedge"
  }
  featureFlags = @{}
  expectedVersion = $tenantConfigFromTerminal.version
} | ConvertTo-Json -Depth 10

$updatedConfig = Invoke-RestMethod `
  -Method Put `
  -Uri "http://localhost:5000/api/v1/tenant/config" `
  -Headers @{ Authorization = "Bearer $($ownerSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $updateConfigBody

$updatedConfig

Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/terminals/$($terminalSession.terminal.id)/revoke" `
  -Headers @{ Authorization = "Bearer $($ownerSession.accessToken)" }

# Expected after revoke: HTTP 401 because the terminal token is no longer active.
Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:5000/api/v1/terminal/session" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" }
```

## PostgreSQL Integration Tests

Database integration tests are disabled unless `SOLIDPOS_TEST_POSTGRES` is configured.

Use a disposable database only. The tests reset the `pos` schema.

PowerShell example:

```powershell
$env:SOLIDPOS_TEST_POSTGRES="Host=localhost;Port=5432;Database=solidpos_test;Username=solidpos;Password=solidpos_dev_password"
dotnet test tests/SolidPOS.PosServer.IntegrationTests/SolidPOS.PosServer.IntegrationTests.csproj
```

Bash example:

```bash
export SOLIDPOS_TEST_POSTGRES="Host=localhost;Port=5432;Database=solidpos_test;Username=solidpos;Password=solidpos_dev_password"
dotnet test tests/SolidPOS.PosServer.IntegrationTests/SolidPOS.PosServer.IntegrationTests.csproj
```

These tests validate:

- migrations apply
- required tables exist
- RLS is enabled
- `digital_receipts` exists
- tenant-scoped RLS isolates store rows
- MVP roles and permissions can be seeded per tenant

## Logs

Logs are JSON to stdout.

Every request should include:

- `trace_id`
- `correlation_id`
- `tenant_id`
- `user_id`
- `terminal_id`
- `store_id`
- `endpoint`
- status code
- elapsed time
- exception stack trace when an error occurs

You can force a correlation id:

```bash
curl -H "X-Correlation-Id: local-test-001" http://localhost:8080/health
```

If something fails, send:

- command used
- full console output
- JSON log lines around the failure
- HTTP response body
- value of `X-Correlation-Id`

Do not send secrets, passwords, access tokens, refresh tokens, enrollment tokens or device tokens.

## Current Macro Phase Status

Implemented:

- monorepo skeleton
- API base
- logging base
- OpenTelemetry base
- health/readiness
- PostgreSQL migrations + RLS verification tests
- MVP role/permission seeds
- JWT configuration
- login scaffold
- refresh-token rotation
- logout refresh-token revocation
- terminal enrollment
- terminal JWTs
- terminal revocation
- runtime active-terminal validation
- tenant POS Builder config runtime API
- tenant config admin update API
- tenant catalog runtime snapshot
- required claims middleware
- `ITenantContext` backed by authenticated claims
- policies generated from permission codes
- Docker local environment
- unit, integration and contract test foundations

Not implemented yet:

- tenant/user provisioning endpoints
- catalog endpoints
- cash shift endpoints
- sales endpoints
- sync push/pull

These come in the next macro phases following `docs/POSSERVER_POSTGRESQL_ROADMAP.md`.

## Iteración actual

- Macro Fase 22: ver `docs/MACRO_PHASE_22_STATUS.md` para endurecimiento de reportes, dashboard read models y semántica de sustitución de ingredientes.

## Macro Fase 25 — Returns / Refunds

Agrega devoluciones POS operativas con reintegro de inventario, refunds, auditoría y lectura/listado:

- `POST /api/v1/returns`
- `GET /api/v1/returns/{returnId}`
- `GET /api/v1/returns?saleId=&from=&to=&limit=`

No incluye SAT/facturación. Es devolución POS interna.

## Macro Fase 26 — Customers API

Esta entrega agrega el modulo operativo de clientes:

- `GET /api/v1/customers`
- `POST /api/v1/customers`
- `GET /api/v1/customers/{customerId}`
- `PATCH /api/v1/customers/{customerId}`
- `GET /api/v1/customers/{customerId}/sales`

La venta ya soportaba `customerId` opcional. Esta fase agrega CRUD basico de cliente e historial comercial con gasto bruto, refunds, gasto neto, ultimo ticket y ticket promedio.

## Macro Fase 28 — Inventory Control Hardening

Agrega políticas configurables de stock negativo, stock counts, transfers, low-stock alerts y reconciliación de inventario sobre el ledger append-only.


## Macro Fase 29 — Sync Conflict Resolution

Se agregó endurecimiento offline-first para conflictos de sincronización:

- `GET /api/v1/sync/conflicts`
- `POST /api/v1/sync/conflicts/{conflictId}/resolve`
- `GET /api/v1/sync/bootstrap`

La fase separa eventos técnicos (`retry_pending`, `dead_letter`) de conflictos de negocio (`conflict`) y permite resolución por `use_server`, `use_client`, `merge` o `compensate`.

## Macro Fase 29 Hotfix 29.1

Corrige la alineación del constructor en `SyncEventProcessingServiceTests` tras agregar `ISyncConflictRepository` al servicio de procesamiento sync.
No modifica runtime ni base de datos.


## Macro Fase 30 — Contract Parity Hardening

Se endureció `SolidPOS.PosServer.ContractTests` para comparar el runtime real contra `contracts/openapi/solidpos-api-v1.openapi.yaml`.

Regla: OpenAPI describe endpoints implementados, no roadmap. Cualquier endpoint nuevo debe agregarse al contrato o los tests de paridad fallarán.

Validaciones agregadas:

- endpoints reales vs OpenAPI por path/method
- rutas documentadas no implementadas
- rutas implementadas no documentadas
- `operationId` obligatorio
- respuesta 2xx obligatoria
- `requestBody` obligatorio en operaciones mutantes cuando aplica
- ProblemDetails via `application/problem+json`

## Macro Fase 31 — Admin/Tenant Management Completion

Adds tenant/settings, stores, users, roles and permissions administrative endpoints. OpenAPI was updated to keep runtime/contract parity enforced by ContractTests.

## Macro Fase 32 — Builder / Updates API

Adds runtime API coverage for Builder projects and POS update release metadata:

- `GET /api/v1/builder/projects`
- `POST /api/v1/builder/projects`
- `POST /api/v1/builder/projects/{projectId}/builds`
- `GET /api/v1/updates/channels`
- `POST /api/v1/updates/releases`
- `GET /api/v1/updates/check`

Updates preserve local branding by contract: `updates/check` returns `brandingPolicy = preserve_local_branding`.

## Macro Fase 33 — Observability + Production Hardening

Agrega observability y hardening pre-producción:

- `GET /health/live`
- `GET /health/ready` con PostgreSQL y tablas runtime críticas
- `GET /api/v1/observability/metrics`
- request metrics in-memory
- DB/sync/sales/payments/inventory/audit metrics
- ProblemDetails estructurado con trace/correlation
- rate limits globales
- CORS explícito
- AllowedHosts estricto en Production
- Forwarded headers para Railway/proxy

Estado: IMPLEMENTED — pending local validation.


## Macro Fase 33 Hotfix 33.1

Fix de compilación en `PostgreSqlOperationalMetricsRepository`: agrega `using Microsoft.Extensions.Configuration;` para habilitar `GetConnectionString("Postgres")`.

## Macro Fase 34 — CI/CD + Deployment Verification

Fase 34 adds delivery gates for PosServer.

### GitHub Actions

Workflow:

```text
.github/workflows/posserver-ci-cd.yml
```

Gates:

```text
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
contract tests
Docker build
PostgreSQL migration smoke test
environment validation
optional Railway deploy
optional post-deploy smoke test
```

### Local deployment environment validation

PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Production"
$env:ASPNETCORE_URLS="http://+:8080"
$env:ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=solidpos;Username=solidpos;Password=solidpos_dev_password"
$env:Jwt__SigningKey="production-like-key-at-least-32-bytes-long"
$env:Jwt__Issuer="SolidPOS"
$env:Jwt__Audience="SolidPOS.PosServer"
$env:AllowedHosts="localhost;*.railway.app"
$env:Cors__AllowedOrigins__0="http://localhost:5173"
.\scripts\validate-deployment-env.ps1
```

Bash:

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://+:8080
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=solidpos;Username=solidpos;Password=solidpos_dev_password'
export Jwt__SigningKey='production-like-key-at-least-32-bytes-long'
export Jwt__Issuer=SolidPOS
export Jwt__Audience=SolidPOS.PosServer
export AllowedHosts='localhost;*.railway.app'
export Cors__AllowedOrigins__0='http://localhost:5173'
bash scripts/validate-deployment-env.sh
```

### Migration smoke test

```bash
export DATABASE_URL="postgresql://solidpos:solidpos_dev_password@localhost:5432/solidpos"
bash scripts/ci/migration-smoke-test.sh
```

### Docker build

```bash
docker build -f deploy/docker/Dockerfile -t solidpos-posserver:local .
```

### Railway

Railway deployment uses:

```text
railway.toml
deploy/docker/Dockerfile
/health/ready
```

Required Railway/GitHub secrets for manual CI deployment:

```text
RAILWAY_TOKEN
RAILWAY_SERVICE_ID
RAILWAY_PROJECT_ID
RAILWAY_ENVIRONMENT_ID
```

Optional smoke-test secrets:

```text
SMOKE_EMAIL
SMOKE_PASSWORD
SMOKE_TENANT_ID
```

Manual post-deploy smoke test:

```powershell
.\scripts\smoke-test-deployment.ps1 -BaseUrl "https://<your-service>.up.railway.app"
```

or:

```bash
bash scripts/smoke-test-deployment.sh "https://<your-service>.up.railway.app"
```

## Macro Fase 34 Hotfix 34.2 — Production deployment hardening

PosServer production deployment accepts either `ConnectionStrings__Postgres` or `DATABASE_URL`. `DATABASE_URL` values such as `postgresql://user:password@host:port/db?sslmode=require` are normalized internally before Npgsql opens a connection.

Railway must use `/health/ready` as the final healthcheck. This endpoint now returns HTTP 503 with a JSON readiness diagnostic body for expected readiness failures instead of leaking an unhandled HTTP 500.

Manual Railway deploys through GitHub Actions now include a `production-migration` job before `railway-deploy`. Configure the GitHub secret `PRODUCTION_DATABASE_URL` before running the manual deployment workflow.

## Macro Fase 34 closure

Macro Fase 34 — CI/CD + Deployment Verification is closed as PASS REAL after validating local build/test, GitHub Actions, Docker build, migration smoke test, Railway deployment, Supabase/PostgreSQL readiness, login, authenticated metrics, and post-deploy smoke test.

Validated deployment URL:

```text
https://full-metal-cash-production.up.railway.app
```

Relevant closure documents:

```text
MACRO_PHASE_34_CLOSURE.md
MACRO_PHASE_34_HOTFIX_34_4.md
```

## Macro Fase 35 — Security / Secrets / Production Auth Hardening

Security hardening artifacts:

```text
MACRO_PHASE_35_SECURITY_HARDENING.md
SECURITY_HARDENING.md
scripts/security/rotate-production-secrets-checklist.md
scripts/security/disable-demo-user.sql
scripts/security/scan-local-secrets.ps1
.github/dependabot.yml
.github/workflows/security-scan.yml
```

Apply the new auth hardening migration:

```powershell
.\scripts\apply-postgresql-migrations.ps1
```

Run local quality gates:

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```

Before production promotion, rotate exposed secrets and disable the demo user after a real administrator has been created and validated.

## SolidPOS Iteration 01 — Production Tenant Provisioning + Admin Bootstrap

Iteration 01 adds production-safe tenant bootstrap endpoints:

```http
GET  /api/v1/provisioning/status
POST /api/v1/provisioning/tenants/bootstrap
```

The bootstrap endpoint is protected with `X-SolidPOS-Provision-Key` and creates a production tenant, initial store, owner admin user, owner role assignments, store access, idempotency record, and audit event. It can also suspend the demo user after the production admin exists.

See:

```text
SOLIDPOS_ITERATION_01_PRODUCTION_BOOTSTRAP.md
ITERATION_01_VALIDATION_COMMANDS.md
```


## SolidPOS Iteration 02 — POS Operational Completion API

Iteration 02 adds operational cash-shift summaries, migration `017_pos_operational_completion.sql`, production POS runtime seeding and an E2E script that validates admin login, terminal enrollment, shift open, sale creation, digital receipt issuing and shift close.

Key files:

```text
database/postgresql/017_pos_operational_completion.sql
scripts/operations/seed-production-pos-runtime.ps1
scripts/operations/validate-production-pos-e2e.ps1
SOLIDPOS_ITERATION_02_POS_OPERATIONAL_COMPLETION.md
ITERATION_02_VALIDATION_COMMANDS.md
```


## SolidPOS Iteration 03 — Offline Sync End-to-End Server Contract

Iteration 03 hardens the server-side offline sync contract with runtime diagnostics, contract discovery, dead-letter inspection/retry, migration `018_sync_e2e_contract_hardening.sql`, and an E2E validation script for push/idempotency/process/pull/status.

Validation guide: `ITERATION_03_VALIDATION_COMMANDS.md`.
