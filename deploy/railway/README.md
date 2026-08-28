# Railway Deployment Verification

Railway deployment targets `SolidPOS.PosServer.Api` through `deploy/docker/Dockerfile`.

## Required Railway variables

Use Railway variables, not checked-in secrets:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
ConnectionStrings__Postgres=<Railway or Supabase PostgreSQL connection string>
Jwt__SigningKey=<minimum 32-byte production secret>
Jwt__Issuer=SolidPOS
Jwt__Audience=SolidPOS.PosServer
AllowedHosts=<your-service>.up.railway.app;*.railway.app
Cors__AllowedOrigins__0=https://<dashboard-domain>
RateLimits__PermitLimit=600
RateLimits__WindowMinutes=1
RateLimits__QueueLimit=0
```

## Health checks

Railway should use:

```text
/health/ready
```

`/health/live` proves the process is alive. `/health/ready` proves PostgreSQL and required runtime tables are ready.

## Deployment gates

Before deploying, GitHub Actions must pass:

```text
dotnet restore
dotnet build
dotnet test
contract tests
docker build
migration smoke test
environment validation
```

## Manual post-deploy smoke test

From a machine with PowerShell:

```powershell
.\scripts\smoke-test-deployment.ps1 -BaseUrl "https://<your-service>.up.railway.app"
```

From bash:

```bash
bash scripts/smoke-test-deployment.sh "https://<your-service>.up.railway.app"
```

## Hotfix 34.2 production rules

PosServer accepts either `ConnectionStrings__Postgres` or `DATABASE_URL`:

```text
ConnectionStrings__Postgres=Host=<host>;Port=<port>;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true
```

or:

```text
DATABASE_URL=postgresql://<user>:<password>@<host>:<port>/<db>?sslmode=require
```

The runtime normalizes `DATABASE_URL` to an Npgsql connection string. Use only one canonical value per environment to avoid confusion.

For Railway health checks, `AllowedHosts` must include the Railway service host and the healthcheck host:

```text
AllowedHosts=<your-service>.up.railway.app;healthcheck.railway.app;*.railway.app
```

Do not use `AllowedHosts=*` as a production solution.

## Production DB migration gate

The workflow has a `production-migration` job that runs only on manual deploy (`workflow_dispatch` with `deploy_railway=true`). It requires this GitHub secret:

```text
PRODUCTION_DATABASE_URL
```

The script refuses local database URLs and validates required runtime tables before Railway deploy starts.

## Readiness behavior

`/health/ready` returns:

- HTTP 200 with `status=ready` when DB and runtime tables are ready.
- HTTP 503 with JSON diagnostic body when DB configuration, connection, or migrations are not ready.

It should not return HTTP 500 for expected deployment-readiness failures.
