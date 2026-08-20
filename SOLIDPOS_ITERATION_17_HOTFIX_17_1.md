# SolidPOS Iteration 17 Hotfix 17.1 — PosBuilder WPF Self-Test Exit

## Estado

Hotfix para desbloquear la validación E2E de Iteration 17.

## Problema

`validate-posbuilder-branding-package.ps1` quedaba detenido en:

```text
Running PosBuilder WPF branding self-test...
```

La causa era que `SolidPOS.PosBuilder.Wpf --self-test` podía quedarse vivo dentro del ciclo WPF/Dispatcher en lugar de cerrar explícitamente el proceso.

## Corrección

- `App.xaml.cs` ahora evalúa `--self-test` antes de `base.OnStartup(e)`.
- El self-test escribe `.runtime/posbuilder-branding-self-test.log`.
- El proceso termina con `Environment.Exit(exitCode)`.
- El script ejecuta PosBuilder WPF con timeout de 15 segundos.
- El script imprime el log generado por el self-test y falla explícitamente si no existe.

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
Builder shell initialized:
Tenant branding package generated:
Branding validation: isValid=True; errors=0; warnings=0
PosBuilder tenant branding package validation completed.
PosCore WPF sales flow QSR validation completed.
message : PosBuilder tenant branding package foundation completed.
```
