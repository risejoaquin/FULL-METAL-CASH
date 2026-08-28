# GA-09 — Validation Commands

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-ga-09-performance-capacity-resilience-offline-readiness.ps1
Unblock-File .\scripts\ga\validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

Select-String .\scripts\ga\validate-ga-09-performance-capacity-resilience-offline-readiness.ps1 -Pattern "GA-09.4-versioned-httpclient-loadtester-isolation"
```

## Ejecutar GA-09 con carga controlada baja

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
  -SkipDashboardBuild
```

## Si ya acabas de pegar GA-08 PASS en este mismo ciclo

Usa esto solo si GA-08 acaba de quedar validado con logs reales y no quieres re-ejecutar GA-08 completo:

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

## Resultado esperado

```text
[GA-09] Validator version GA-09.4-versioned-httpclient-loadtester-isolation
[GA-09] Repository/source guardrails PASS
[GA-09] Local build/test/secret guardrails PASS
[GA-09] Production authentication and baseline protected endpoint checks PASS
[GA-09] Database pre-load integrity/capacity snapshot PASS
[GA-09] Controlled load: health/readiness...
[GA-09] Controlled load: protected read endpoints...
[GA-09] Controlled resilience/idempotency negative retry checks PASS
[GA-09] Database post-load integrity/capacity snapshot PASS
[GA-09] GA-09 evidence manifest and performance snapshot PASS
[GA-09] GA-09 PASS GA PERFORMANCE CAPACITY RESILIENCE OFFLINE READINESS / GO GA-10
```

## Logs que debes pegar si falla

Pega completo:

- bloque de PowerShell desde `[GA-09] Validator version...` hasta el error;
- si falla build/test, el bloque completo de `dotnet`;
- si falla SQL, el mensaje de `Database SQL failed` y la línea anterior;
- si falla health/ready, estado HTTP exacto;
- si falla por p95/p99/error rate, pega las líneas `Health load...` y `Protected read load...`;
- logs Railway de PosServer del mismo minuto del fallo.

No pegues passwords, tokens, refresh tokens ni `DATABASE_URL`.

## Hotfix GA-09.3 — Endpoint error breakdown diagnostics

Use this when GA-09.2 reports low p95 but high error percent.

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-ga-09-performance-capacity-resilience-offline-readiness.ps1
Select-String .\scripts\ga\validate-ga-09-performance-capacity-resilience-offline-readiness.ps1 -Pattern "GA-09.4-versioned-httpclient-loadtester-isolation"

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
