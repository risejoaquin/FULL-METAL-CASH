# SolidPOS Pilot Rollback Plan

## Purpose

Define the operational rollback path for the controlled pilot.

## Rollback triggers

Rollback must be considered if any of these occur:

- Production readiness is down and cannot recover quickly.
- Supabase connection fails after redeploy.
- Admin login fails for the pilot tenant.
- Sales cannot be retrieved after being created.
- Receipts cannot be retrieved after issuance.
- Dashboard cannot be built or opened during the pilot window.
- Sync runtime status is unavailable.
- Data consistency issue appears in sales, payments, cash, or inventory.

## Rollback actions

1. Stop processing pilot transactions.
2. Export or record current pilot state.
3. Check Railway deployment history.
4. Redeploy the last known good Railway deployment.
5. Confirm `/health/ready` returns `ready`.
6. Confirm admin login works.
7. Confirm dashboard build/self-test passes locally.
8. Confirm sales and audit read models are reachable.
9. Decide GO/NO-GO for resuming pilot.

## Stable reference

The repository should keep a stable tag for the pilot-ready baseline:

```powershell
git tag -a v0.1.0-pilot-ready -m "SolidPOS production pilot ready"
git push origin v0.1.0-pilot-ready
```

## Post-rollback validation

Run:

```powershell
.\scripts\pilot\validate-controlled-store-pilot-setup.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL
```

## PILOT-09 backup and restore reference

Rollback decisions during pilot incidents must reference the latest validated backup and restore drill. For SolidPOS pilot operations, backup evidence comes from PILOT-08 Backup / Restore / Rollback Drill. Restore must be validated in an isolated PostgreSQL container before any production-impacting recovery decision. No destructive production restore is allowed without explicit owner approval and a fresh backup manifest.
