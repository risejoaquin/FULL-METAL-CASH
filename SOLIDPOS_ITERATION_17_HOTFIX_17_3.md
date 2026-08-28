# SolidPOS Iteration 17 Hotfix 17.3 — PosBuilder Self-Test Exit Code Capture

## Estado

Hotfix 17.2 hizo que el self-test de PosBuilder terminara y escribiera el log correctamente, pero el script de validación seguía fallando porque `Start-Process` devolvía `ExitCode` vacío para el entrypoint WPF/WinExe.

## Corrección

Se actualizó `scripts/poscore/validate-posbuilder-branding-package.ps1` para ejecutar el self-test con invocación directa de `dotnet run` y leer el código real desde `$LASTEXITCODE`.

## Impacto

- No modifica PosServer.
- No modifica SQLite.
- No modifica contrato de branding.
- No modifica PosCore WPF.
- Solo corrige la captura del exit code del self-test PosBuilder.

## Validación

```powershell
dotnet restore solidpos-platform.sln
dotnet build solidpos-platform.sln
dotnet test solidpos-platform.sln
.\scripts\poscore\validate-posbuilder-branding-package.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -TenantName "Mi Cafeteria" `
  -AppName "Mi Cafeteria POS" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -TerminalId "AUTO"
```

## Resultado esperado

```text
PosBuilder branding self-test started.
Builder shell initialized: Branding package valido para Mi Cafeteria POS.
Tenant branding package generated: tenantName=Mi Cafeteria; appName=Mi Cafeteria POS
Branding validation: isValid=True; errors=0; warnings=0
PosBuilder tenant branding package validation completed.
PosCore WPF sales flow QSR validation completed.
message : PosBuilder tenant branding package foundation completed.
```
