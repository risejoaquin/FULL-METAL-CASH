# GA-06 Validation Commands

## 1. Restore/build/test locally
```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
dotnet restore .\solidpos-platform.sln
dotnet build .\solidpos-platform.sln --no-restore
dotnet test .\solidpos-platform.sln --no-build
```

## 2. Apply PostgreSQL migration 019 to production
```powershell
.\scripts\apply-postgresql-migrations.ps1 -DatabaseUrl $env:DATABASE_URL
```

Migration is idempotent. Do not use `-ResetSchema` against production.

## 3. Deploy this GA-06 backend build to Railway
The production API must include the new `targetTerminalIds` / `terminalId` cohort contract before the validator creates the stable release. Use the repository's normal Railway deployment path, then verify `/health/ready`.

## 4. Unblock validators
```powershell
Unblock-File .\scripts\ga\validate-ga-06-stable-channel-promotion-cohort-update-dry-run.ps1
Unblock-File .\scripts\ga\validate-ga-04-production-data-integrity-financial-reconciliation.ps1
Unblock-File .\scripts\ga\validate-ga-03-support-incident-slo-operations-readiness.ps1
Unblock-File .\scripts\ga\validate-ga-02-sync-queue-sla-closure.ps1
Unblock-File .\scripts\ga\validate-ga-01-general-availability-baseline-freeze.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

## 5. Execute GA-06
```powershell
.\scripts\ga\validate-ga-06-stable-channel-promotion-cohort-update-dry-run.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ReleaseVersion "1.0.0-rc.1" `
  -SkipDashboardBuild
```

Expected final line:
`[GA-06] GA-06 PASS GA STABLE CHANNEL PROMOTION COHORT DRY RUN / GO GA-07`
