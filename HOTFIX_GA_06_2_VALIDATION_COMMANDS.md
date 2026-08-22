# HOTFIX GA-06.2 — DatabaseUrl migration target correctness

## Cause
When local `psql` was unavailable, `scripts/apply-postgresql-migrations.ps1` ignored a supplied `-DatabaseUrl` and executed migrations against the local `solidpos-postgres` container. GA-06 correctly queried the supplied production DatabaseUrl, therefore migration 019 was absent there.

## Fix
If `DatabaseUrl` is provided:
- local `psql` remains first choice;
- otherwise Docker is used only as a PostgreSQL client (`postgres:17`) connecting to that exact `DatabaseUrl`;
- the local `solidpos-postgres` fallback is used only when no DatabaseUrl is provided.

## Apply migration to the intended database
```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
Unblock-File .\scripts\apply-postgresql-migrations.ps1
.\scripts\apply-postgresql-migrations.ps1 -DatabaseUrl $env:DATABASE_URL
```

Expected GA-06 migration evidence must no longer show `docker cp ... solidpos-postgres` when DatabaseUrl is supplied. It should use Docker only as the client to the supplied connection string.

## Re-run GA-06
```powershell
Unblock-File .\scripts\ga\validate-ga-06-stable-channel-promotion-cohort-update-dry-run.ps1
.\scripts\ga\validate-ga-06-stable-channel-promotion-cohort-update-dry-run.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ReleaseVersion "1.0.0-rc.1" `
  -SkipDashboardBuild
```

Required final result:
`[GA-06] GA-06 PASS GA STABLE CHANNEL PROMOTION COHORT DRY RUN / GO GA-07`
