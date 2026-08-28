# SolidPOS PILOT-07 - Dashboard Operations Monitoring Validation Commands

Estado esperado antes de ejecutar: PILOT-06 PASS REAL PRODUCTION / GO.

## 1. Cargar secretos locales

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```

Resultado esperado:

```text
postgresql://
```

## 2. Unblock scripts

```powershell
Unblock-File .\scripts\pilot\validate-dashboard-operations-monitoring.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

## 3. Ejecutar PILOT-07

```powershell
.\scripts\pilot\validate-dashboard-operations-monitoring.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL
```

## 4. Ejecutar sin npm install si ya fue instalado

```powershell
.\scripts\pilot\validate-dashboard-operations-monitoring.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipNpmInstall
```

## Resultado esperado

```text
[PILOT-07] PILOT-07 PASS REAL PRODUCTION / GO
```

## Logs si falla

Enviar:

```text
Salida completa de PowerShell desde el primer [PILOT-07]
docs/pilot/logs/pilot-07-dashboard-operations-monitoring-log.md si existe
src/PosDashboard/SolidPOS.PosDashboard.Admin/package.json
src/PosDashboard/SolidPOS.PosDashboard.Admin/src/api/posServerClient.ts
src/PosDashboard/SolidPOS.PosDashboard.Admin/src/features/dashboard/OperationsDashboard.tsx
```


## HOTFIX 07.2

Si vienes de HOTFIX 07.1 y fallo el SQL cross-check con `LINE 1: -v`, aplica el ZIP HOTFIX 07.2 y repite el comando principal.

Resultado esperado inmediato:

```text
[PILOT-07] SQL cross-check against operations metrics PASS
```
