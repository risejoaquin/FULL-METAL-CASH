# HOTFIX GA-06.8 — Explicit release reconciliation conflict diagnostics

## Objetivo
Distinguir de forma segura las dos causas de rechazo de `POST /api/v1/updates/releases` durante GA-06:

- `INVALID_TARGET_TERMINAL`
- `RELEASE_IDENTITY_CONFLICT`

El hotfix no relaja los controles de identidad, no modifica releases existentes y no borra targets.

## Build / test
```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

dotnet restore .\solidpos-platform.sln
dotnet build .\solidpos-platform.sln --no-restore
dotnet test .\solidpos-platform.sln --no-build
```

## Migraciones
No hay migraciones nuevas.

## Deploy
Desplegar PosServer de este repo en Railway porque cambia Application, Infrastructure y Api.

## Validación
```powershell
Unblock-File .\scripts\ga\validate-ga-06-stable-channel-promotion-cohort-update-dry-run.ps1

.\scripts\ga\validate-ga-06-stable-channel-promotion-cohort-update-dry-run.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ReleaseVersion "1.0.0-rc.1" `
  -SkipDashboardBuild
```

Si sigue existiendo conflicto, el log debe incluir un body como:

```text
{"title":"Update release creation conflict","errorCode":"RELEASE_IDENTITY_CONFLICT","conflictingFields":["rollbackVersion"],...}
```

o:

```text
{"title":"Update release creation conflict","errorCode":"INVALID_TARGET_TERMINAL","conflictingFields":["targetTerminalIds"],...}
```
