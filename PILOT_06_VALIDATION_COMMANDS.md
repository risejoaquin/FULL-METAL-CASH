# SolidPOS PILOT-06 Validation Commands

Estado objetivo:

```text
SolidPOS PILOT-06 - Sync Recovery / Conflict Field Test = PENDING USER VALIDATION
```

## 1. Ubicacion

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
```

## 2. Secretos locales

No pegar secretos en la terminal compartida.

```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```

Resultado esperado:

```text
postgresql://
```

## 3. Quitar bloqueo de Windows a scripts usados por PILOT-06

```powershell
Unblock-File .\scripts\pilot\validate-sync-recovery-conflict-field-test.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1
```

## 4. Build local

```powershell
dotnet build solidpos-platform.sln
```

Resultado esperado:

```text
0 errors
```

## 5. Tests locales

```powershell
dotnet test solidpos-platform.sln
```

Resultado esperado:

```text
Failed: 0
```

## 6. Ejecutar PILOT-06

```powershell
.\scripts\pilot\validate-sync-recovery-conflict-field-test.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -StoreCode "MAIN"
```

Resultado esperado:

```text
[PILOT-06] PILOT-06 PASS REAL PRODUCTION / GO
```

## 7. Opcion si el dashboard ya fue validado y quieres aislar sync

```powershell
.\scripts\pilot\validate-sync-recovery-conflict-field-test.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -StoreCode "MAIN" `
  -SkipDashboardValidation
```

## Logs si falla

Enviar:

```text
Salida completa PowerShell desde el primer [PILOT-06]
docs/pilot/logs/pilot-06-sync-recovery-conflict-field-test-log.md si existe
.runtime/pilot-06-sync-recovery-conflict-field-test.sqlite
.runtime/pilot-06-sync-recovery-conflict-field-test.sqlite-wal si existe
.runtime/pilot-06-sync-recovery-conflict-field-test.sqlite-shm si existe
```

## HOTFIX 06.1

Si vienes del ZIP anterior, reemplaza el repo con `solidpos-platform-pilot-06-hotfix-06-1-poscore-bind-fingerprint-20260820.zip` y repite el mismo comando principal. Este hotfix corrige el contrato real de `bind` agregando `--fingerprint`.
