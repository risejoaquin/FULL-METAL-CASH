# HOTFIX GA-06.9 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

dotnet restore .\solidpos-platform.sln
```

```powershell
dotnet build .\solidpos-platform.sln --no-restore
```

```powershell
dotnet test .\solidpos-platform.sln --no-build
```

No database migration is introduced by GA-06.9.
Deploy the PosServer from this complete repo to Railway, verify `/health/ready`, then run:

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

Expected final gate:

`[GA-06] GA-06 PASS GA STABLE CHANNEL PROMOTION COHORT DRY RUN / GO GA-07`
