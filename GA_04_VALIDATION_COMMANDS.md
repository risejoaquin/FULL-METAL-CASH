# GA-04 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
Unblock-File .\scripts\ga\validate-ga-04-production-data-integrity-financial-reconciliation.ps1
Unblock-File .\scripts\ga\validate-ga-03-support-incident-slo-operations-readiness.ps1
Unblock-File .\scripts\ga\validate-ga-02-sync-queue-sla-closure.ps1
Unblock-File .\scripts\ga\validate-ga-01-general-availability-baseline-freeze.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\ga\validate-ga-04-production-data-integrity-financial-reconciliation.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected final line:
`[GA-04] GA-04 PASS GA PRODUCTION DATA INTEGRITY FINANCIAL RECONCILIATION / GO GA-05`
