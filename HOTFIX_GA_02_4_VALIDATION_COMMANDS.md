# HOTFIX GA-02.4 validation commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
Unblock-File .\scripts\ga\validate-ga-02-sync-queue-sla-closure.ps1
Unblock-File .\scripts\ga\validate-ga-01-general-availability-baseline-freeze.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

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
