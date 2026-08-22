# GA-01 Validation Commands

## 1. Restore

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
dotnet restore solidpos-platform.sln
```

Expected: restore completes with 0 errors.

## 2. Build

```powershell
dotnet build solidpos-platform.sln
```

Expected: build completes with 0 errors.

## 3. Tests

```powershell
dotnet test solidpos-platform.sln
```

Expected: all test projects PASS, 0 failed.

## 4. Production credentials

```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```

Expected prefix: `postgresql://` or `postgres://`. Do not print the full secret.

## 5. Unblock validators

```powershell
Unblock-File .\scripts\ga\validate-ga-01-general-availability-baseline-freeze.ps1
Unblock-File .\scripts\beta\validate-beta-10-limited-commercial-beta-closure-report.ps1
Unblock-File .\scripts\beta\validate-beta-09-data-quality-reconciliation-closure.ps1
Unblock-File .\scripts\beta\validate-beta-08-customer-acceptance-validation.ps1
Unblock-File .\scripts\beta\validate-beta-07-dashboard-daily-monitoring-pack.ps1
Unblock-File .\scripts\expansion\validate-exp-06-inventory-reconciliation-hardening.ps1
Unblock-File .\scripts\expansion\validate-exp-05-operational-monitoring-hardening.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

## 6. GA-01 production validator

```powershell
.\scripts\ga\validate-ga-01-general-availability-baseline-freeze.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

`-SkipDashboardBuild` is allowed because inherited BETA validators still enforce the dashboard source contract.

Expected final line:

```text
[GA-01] GA-01 PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02
```

## Runtime evidence expected

```text
.runtime/ga-01-general-availability-baseline-freeze/
  ga-01-manifest.json
  ga-01-evidence.md
  ga-01-snapshot.json
```

## If it fails

Send the complete PowerShell output from the first `[GA-01]` line through the exception, plus these files if created:

```text
.runtime/ga-01-general-availability-baseline-freeze/ga-01-manifest.json
.runtime/ga-01-general-availability-baseline-freeze/ga-01-snapshot.json
.runtime/beta-10-limited-commercial-beta-closure-report/beta-10-limited-commercial-beta-closure-manifest.json
docs/ga/logs/ga-01-general-availability-baseline-freeze-log.md
```

Do not advance to GA-02 until real production logs show the required PASS.
