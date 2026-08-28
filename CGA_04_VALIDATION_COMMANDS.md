# CGA-04 Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-cga-04-public-ga-activation-decision.ps1
Unblock-File .\scripts\ga\validate-cga-03-capacity-db-remediation-or-formal-acceptance.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

Select-String .\scripts\ga\validate-cga-04-public-ga-activation-decision.ps1 -Pattern "CGA-04.1-sync-contract-schema-version-compatibility"

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString

if ([string]::IsNullOrWhiteSpace($env:DATABASE_URL)) {
  throw "DATABASE_URL no está configurado en esta sesión."
} else {
  "DATABASE_URL presente en sesión: OK"
}

.\scripts\ga\validate-cga-04-public-ga-activation-decision.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -RolloutMode LIMITED `
  -Decision KEEP_LIMITED_GA `
  -MaxStores 2 `
  -MaxConcurrentTerminals 2 `
  -CapacitySampleCount 12 `
  -MaxP95LatencyMs 5000 `
  -AllowedExistingSyncConflictCount 3 `
  -AllowedDeadLetterCount 1 `
  -AllowedWaitingConnectionCount 11 `
  -SkipDashboardBuild `
  -SkipCga03Revalidation
```


## HOTFIX-01 — Sync contract schema version compatibility

CGA-04.1 accepts the production sync contract field `currentSchemaVersion` as the canonical schema version and falls back from legacy `schemaVersion` only for compatibility. This does not change backend contracts, does not activate Public GA, and preserves the requirement that the resolved schema version is 4 and `schema_version_4` remains the accepted sync contract.
