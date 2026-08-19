# Macro Fase 34 — Hotfix 34.4

## Title

Supabase-compatible dev/demo seed + Phase 34 closure documentation.

## Status

IMPLEMENTED — pending local build/test and GitHub/Railway confirmation after applying this ZIP.

## Context

Macro Fase 34 was validated end-to-end after Hotfixes 34.1–34.3:

```text
Local restore/build/test                 PASS
GitHub Actions quality gates             PASS
GitHub Actions migration smoke test      PASS
Railway Docker build                     PASS
Railway deployment                       PASS
Supabase/PostgreSQL real connection      PASS
Remote migrations                        PASS
GET /health/live                         PASS
GET /health/ready                        PASS
POST /api/v1/auth/login                  PASS after demo seed patch
GET /api/v1/observability/metrics        PASS
Post-deploy smoke test                   PASS
```

Evidence from the operational validation:

```text
Deployment smoke test passed for https://full-metal-cash-production.up.railway.app.
```

## Problem found

The development/demo seed was not portable across PostgreSQL providers.

Local PostgreSQL exposed `pgcrypto` functions through `public`, while Supabase exposed them through the `extensions` schema:

```text
schema     | function_name
-----------+--------------
extensions | crypt
extensions | gen_salt
```

Because `004_seed_dev_auth.sql` used `public.crypt(...)` and `public.gen_salt(...)`, the seed failed on Supabase with:

```text
ERROR: function public.gen_salt(unknown, integer) does not exist
```

## Changes

### Updated file

```text
database/postgresql/004_seed_dev_auth.sql
```

### Before

```sql
SET search_path TO pos, public;
public.crypt('Admin123!', public.gen_salt('bf', 12))
```

### After

```sql
SET search_path TO pos, extensions, public;
crypt('Admin123!', gen_salt('bf', 12))
```

## Architectural decision

The demo seed should be provider-compatible without hard-coding the pgcrypto schema.

The `search_path` now resolves functions in this order:

```text
pos
extensions
public
```

This supports:

```text
Supabase: pgcrypto functions in extensions
Local PostgreSQL: pgcrypto functions in public
```

## Scope control

This hotfix does not change:

```text
business logic
sales
inventory
auth runtime behavior
JWT contracts
OpenAPI business endpoints
Railway Dockerfile
health/readiness logic
production migrations
```

It only fixes the development/demo seed portability and documents the final closure of Fase 34.

## Validation commands

### Local build/test

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```

### Apply demo seed to Supabase/PostgreSQL real DB

```powershell
$env:DATABASE_URL = Read-Host "Pega DATABASE_URL"

docker run --rm `
  --env "DATABASE_URL=$env:DATABASE_URL" `
  -v "${PWD}:/work" `
  -w /work `
  postgres:16 `
  psql "$env:DATABASE_URL" -v ON_ERROR_STOP=1 -f database/postgresql/004_seed_dev_auth.sql
```

Expected:

```text
COMMIT
```

### Verify demo user

```powershell
docker run --rm `
  --env "DATABASE_URL=$env:DATABASE_URL" `
  postgres:16 `
  psql "$env:DATABASE_URL" -c "select id, email, status, tenant_id, password_hash is not null as has_hash, length(password_hash) as hash_length from pos.users where email = 'owner@solidpos.local';"
```

Expected:

```text
owner@solidpos.local | active | t | 60
```

### Post-deploy smoke test

```powershell
.\scripts\smoke-test-deployment.ps1 -BaseUrl "https://full-metal-cash-production.up.railway.app"
```

Expected:

```text
Checking liveness...
Checking readiness...
Checking authenticated metrics...
Deployment smoke test passed for https://full-metal-cash-production.up.railway.app.
```

## Final Fase 34 status

```text
Macro Fase 34 — CI/CD + Deployment Verification = PASS REAL
```

## Remaining security action

Because a database password was exposed during manual debugging, rotate the Supabase/Railway database password before treating this as real production.
