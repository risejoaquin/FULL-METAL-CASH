# PILOT-05 Validation Commands

## 1. Preparar variables

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

## 2. Restore

```powershell
dotnet restore solidpos-platform.sln
```

Resultado esperado:

```text
Restore completed / todos los proyectos actualizados para restauración
```

## 3. Build

```powershell
dotnet build solidpos-platform.sln
```

Resultado esperado:

```text
Build succeeded / 0 errores
```

## 4. Test

```powershell
dotnet test solidpos-platform.sln
```

Resultado esperado:

```text
Correctas / Passed en todas las suites
```

## 5. Migraciones PostgreSQL

```powershell
.\scripts\apply-postgresql-migrations.ps1
```

Resultado esperado:

```text
Migraciones aplicadas o ya existentes, sin errores
```

## 6. Seed dev/auth si estás probando local

```powershell
.\scripts\apply-dev-auth-seed.ps1
```

Resultado esperado:

```text
Seed aplicado o ya existente, sin errores
```

## 7. Levantar API local si validarás localmente

```powershell
.\scripts\run-posserver-dev.ps1
```

Resultado esperado:

```text
PosServer escuchando en el puerto configurado
```

## 8. Ejecutar PILOT-05 contra producción

```powershell
.\scripts\pilot\validate-offline-mode-field-test.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -StoreCode "MAIN" `
  -ProductSku "QSR-AMERICANO"
```

Resultado esperado:

```text
[PILOT-05] PILOT-05 PASS REAL PRODUCTION / GO
```

## 9. Variante si el dashboard falla por tooling frontend

```powershell
.\scripts\pilot\validate-offline-mode-field-test.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -StoreCode "MAIN" `
  -ProductSku "QSR-AMERICANO" `
  -SkipDashboardValidation
```

## 10. Logs que debes enviar si falla

Enviar completos:

```text
Salida completa de PowerShell desde el primer [PILOT-05]
docs/pilot/logs/pilot-05-offline-mode-field-test-log.md
.runtime/pilot-05-offline-mode-field-test.sqlite si el fallo es local/offline
.runtime/pilot-05-offline-mode-field-test.sqlite-wal si existe
.runtime/pilot-05-offline-mode-field-test.sqlite-shm si existe
```

Si falla SQL, enviar también la salida completa del bloque `SQL validation`.

Si falla sync, enviar:

```powershell
# sustituye terminalId por el terminalId mostrado por el script si alcanzó a imprimirlo
Invoke-RestMethod -Method Get -Uri "https://full-metal-cash-production.up.railway.app/api/v1/sync/status?storeId=8e446c29-e9ad-41ed-a738-125aff7608b6&terminalId=<terminalId>" -Headers $adminHeaders
```


## HOTFIX 05.3 note

If Windows shows the script trust warning, unblock the validator before running:

```powershell
Unblock-File .\scripts\pilotalidate-offline-mode-field-test.ps1
```

Then run the same validation command. HOTFIX 05.3 only changes the PowerShell parser-safe log writer.

## HOTFIX 05.4 command note

If Windows shows downloaded-script warnings for nested scripts, run:

```powershell
Unblock-File .\scripts\pilot\validate-offline-mode-field-test.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1
```

Then rerun the same PILOT-05 command.
