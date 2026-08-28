# HOTFIX GA-06.6 — Validation Commands

## 1. Restore / build / test

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

dotnet restore .\solidpos-platform.sln
dotnet build .\solidpos-platform.sln --no-restore
dotnet test .\solidpos-platform.sln --no-build
```

Expected: build succeeds with 0 errors and all tests pass.

## 2. Database

No new migration is required. Migration 019 must already exist in production.

## 3. Deploy

Deploy this complete repository to Railway because `SolidPOS.PosServer.Infrastructure` changed.

Verify:

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri "https://full-metal-cash-production.up.railway.app/health/ready"
```

Expected: `status=ready`, `database=ready`.

## 4. GA-06

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

Expected progression:

```text
[GA-06] Promote identical RC through internal -> beta -> stable for one controlled terminal...
[GA-06] Promote identical RC through internal -> beta -> stable PASS
[GA-06] Targeted update-check matrix PASS
...
[GA-06] GA-06 PASS GA STABLE CHANNEL PROMOTION COHORT DRY RUN / GO GA-07
```
