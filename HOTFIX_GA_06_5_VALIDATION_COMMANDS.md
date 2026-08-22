# HOTFIX GA-06.5 — Terminal collection response normalization

## Failure corrected
GA-06.4 reached cohort selection, but `/api/v1/terminals` returns a collection wrapper (`items`) rather than a raw array. Wrapping the response object with `@(...)` caused PowerShell property enumeration to concatenate all terminal IDs into one scalar string, which later failed the PostgreSQL `uuid` cast.

## Change
`validate-ga-06-stable-channel-promotion-cohort-update-dry-run.ps1` now normalizes collection responses through `Get-Items`, matching the established EXP-10/EXP-12 validator pattern. GA-06 then selects exactly one active target terminal and one distinct active outside-cohort terminal.

No database, API, migration, release, schema, or rollout behavior changes.

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

Required final result:
`[GA-06] GA-06 PASS GA STABLE CHANNEL PROMOTION COHORT DRY RUN / GO GA-07`
