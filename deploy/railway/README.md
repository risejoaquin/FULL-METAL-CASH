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
