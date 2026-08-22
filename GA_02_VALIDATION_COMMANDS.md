# GA-02 Validation Commands

## 1. Repository
```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
```

## 2. Optional explicit restore/build/test
GA-02 revalidates GA-01, which already executes the established build/test chain through its BETA prerequisites. You may still run:
```powershell
dotnet restore solidpos-platform.sln
```
```powershell
dotnet build solidpos-platform.sln
```
```powershell
dotnet test solidpos-platform.sln
```

## 3. Production secrets
```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
```

## 4. Unblock
```powershell
Unblock-File .\scripts\ga\validate-ga-02-sync-queue-sla-closure.ps1
Unblock-File .\scripts\ga\validate-ga-01-general-availability-baseline-freeze.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

## 5. Execute GA-02
```powershell
.\scripts\ga\validate-ga-02-sync-queue-sla-closure.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected final line:
```text
[GA-02] GA-02 PASS GA SYNC QUEUE SLA CLOSURE / GO GA-03
```

If it fails, send the full PowerShell output including the printed `Retry details` / `Dead-letter details` blocks. Do not manually delete or edit sync rows.
