# Macro Fase 34 — CI/CD + Deployment Verification

Status: IMPLEMENTED — pending local/remote validation

## Objective

Move validation beyond local execution and enforce a delivery pipeline for PosServer:

- GitHub Actions
- `dotnet restore`
- `dotnet build`
- `dotnet test`
- contract tests
- Docker build
- migration smoke test
- Railway deployment path
- environment validation
- post-deploy smoke testing

## Implemented artifacts

```text
.github/workflows/posserver-ci-cd.yml
.dockerignore
railway.toml
deploy/railway/README.md
deploy/railway/env.production.example
scripts/wait-for-postgres.sh
scripts/ci/migration-smoke-test.sh
scripts/validate-deployment-env.sh
scripts/validate-deployment-env.ps1
scripts/smoke-test-deployment.sh
scripts/smoke-test-deployment.ps1
scripts/apply-postgresql-migrations.sh
README.md
MACRO_PHASE_34_STATUS.md
```

## CI/CD gates

The workflow now defines the following gates:

```text
dotnet-quality-gate
  dotnet restore
  dotnet build --configuration Release
  dotnet test --configuration Release

environment-validation
  validates required production env variables
  rejects wildcard AllowedHosts in Production
  validates JWT signing key length

docker-build
  builds deploy/docker/Dockerfile

migration-smoke-test
  starts PostgreSQL 16 service
  waits for DB readiness
  applies PostgreSQL migrations 001–014
  applies dev auth seed
  verifies critical runtime tables
  verifies stable/beta/internal update channels

railway-deploy
  manual workflow_dispatch only
  requires Railway secrets
  runs railway up --service ... --detach

post-deploy-smoke-test
  optional after Railway deployment
  verifies /health/live
  verifies /health/ready
  logs in
  verifies /api/v1/observability/metrics
```

## Architectural decision

Deployment is not considered valid because the app compiles locally.

The release gate is now:

```text
restore
+ build
+ all tests
+ OpenAPI contract parity
+ Docker image build
+ migration smoke test
+ environment validation
+ health/readiness verification
+ post-deploy smoke test
```

## Railway hardening

Added `railway.toml` with Dockerfile builder and `/health/ready` as the health check path.

Production variables are documented in:

```text
deploy/railway/env.production.example
deploy/railway/README.md
```

## Migration smoke test

The bash migration script now includes:

```text
database/postgresql/014_builder_updates_runtime.sql
```

This keeps Linux CI aligned with the Windows PowerShell migration script.

## Pending validation

```text
Local build/test after Fase 34              PENDIENTE
GitHub Actions syntax/execution             PENDIENTE
Docker build from workflow                  PENDIENTE
Migration smoke test in GitHub Actions      PENDIENTE
Railway deployment                          PENDIENTE
Post-deploy smoke test                      PENDIENTE
```
