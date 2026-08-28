# HOTFIX LGA-07.1 Validation Commands — DB Pressure Diagnostics

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-lga-07-hotfix-07-1-db-pressure-diagnostics.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

.\scripts\ga\validate-lga-07-hotfix-07-1-db-pressure-diagnostics.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -AllowedWaitingConnectionCount 12 `
  -DiagnosticEscalationConnectionCount 13 `
  -SkipDashboardBuild
```

Expected status:

```text
PASS HOTFIX LGA-07.1 DB PRESSURE DIAGNOSTICS / LGA-07 REMAINS PENDING WHEN WAITING CONNECTIONS EXCEED BASELINE
```

If `waitingConnectionCount >= 13`, do not raise the baseline. Keep LGA-07 pending and execute capacity upgrade or DB pool remediation.
