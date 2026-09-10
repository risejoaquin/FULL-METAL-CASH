# V1.1-03 Validation Commands

## Static contract

```powershell
Unblock-File .\scripts\v1.1\validate-v1.1-03-advanced-health-readiness.ps1
.\scripts\v1.1\validate-v1.1-03-advanced-health-readiness.ps1 -StaticOnly
```

Esperado:

`PASS V1.1-03 ADVANCED HEALTH READINESS STATIC CONTRACT`

## CI

GitHub Actions `SolidPOS CI` ejecuta secret scan, V1.1-02 static contract, V1.1-03 static contract, restore, build y tests.

## Producción

```powershell
.\scripts\v1.1\validate-v1.1-03-advanced-health-readiness.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -Concurrency 3 `
  -Requests 6 `
  -MaxReadinessP95Ms 1200
```

El validator exige schemaVersion 4, syncContract `schema_version_4`, schema compatible, sync ready, dependency breakdown seguro y capacity gate con p95 <= 1200 ms.
