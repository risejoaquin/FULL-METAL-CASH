# HOTFIX GA-06.10 — Transactional DB JSON Output Parsing

## Scope
Validator-only hardening. No database migration and no PosServer redeploy.

## Cause
`psql -f` emits transaction command tags such as `ROLLBACK` after the JSON row. The previous helper selected the last non-empty line and attempted `ConvertFrom-Json`, so a valid rollback drill failed while parsing the command tag.

## Fix
`Invoke-DbJsonFile` now scans output from bottom to top, ignores transaction/control tags (`BEGIN`, `COMMIT`, `ROLLBACK`, `SET`), and returns only a line that successfully parses as JSON. A successful psql exit with no JSON remains a hard failure.

## Run
```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
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

## Required final result
`[GA-06] GA-06 PASS GA STABLE CHANNEL PROMOTION COHORT DRY RUN / GO GA-07`
