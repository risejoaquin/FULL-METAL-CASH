# SolidPOS — Hotfix GA-09.1 validation commands

## 1. Preparar PowerShell

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-ga-09-performance-capacity-resilience-offline-readiness.ps1
Unblock-File .\scripts\ga\validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

Select-String .\scripts\ga\validate-ga-09-performance-capacity-resilience-offline-readiness.ps1 -Pattern "GA-09.1-powershell-error-variable-type-safety"
```

## 2. Ejecutar GA-09

```powershell
$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString

.\scripts\ga\validate-ga-09-performance-capacity-resilience-offline-readiness.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -HealthRequests 24 `
  -ProtectedRequests 18 `
  -Concurrency 4 `
  -P95ThresholdMs 2500 `
  -P99ThresholdMs 5000 `
  -MaxErrorPercent 2 `
  -SkipDashboardBuild `
  -SkipGa08Revalidation
```

## 3. Resultado esperado

```text
[GA-09] GA-09 evidence manifest and performance snapshot PASS
[GA-09] GA-09 PASS GA PERFORMANCE CAPACITY RESILIENCE OFFLINE READINESS / GO GA-10
```

## 4. Logs a enviar si falla

Enviar el log completo desde:

```text
[GA-09] Validator version GA-09.1-powershell-error-variable-type-safety
```

hasta el error final. No pegar passwords, tokens, refresh tokens ni DATABASE_URL.
