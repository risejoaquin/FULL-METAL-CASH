# HOTFIX GA-08.1 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\apply-postgresql-migrations.ps1

.\scripts\apply-postgresql-migrations.ps1 `
  -DatabaseUrl $env:DATABASE_URL
```

Expected production path:

```text
Existing pos schema detected...
GA-06/019 migration marker detected. Skipping historical migrations 002-019 and evaluating GA-08/020 only.
Applying database/postgresql/020_ga08_complete_tenant_rls.sql
PostgreSQL migrations applied.
```

Then verify `/health/ready` and rerun GA-08 validator.
