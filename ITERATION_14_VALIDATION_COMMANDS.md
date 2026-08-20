# SolidPOS Iteration 14 — Validation Commands

## Restore

```powershell
dotnet restore solidpos-platform.sln
```

## Build

```powershell
dotnet build solidpos-platform.sln
```

Expected:

```text
Compilación correcta.
0 Advertencia(s)
0 Errores
```

## Tests

```powershell
dotnet test solidpos-platform.sln
```

Expected: all PosCore and PosServer tests pass.

## WPF Shell Self-Test

```powershell
.\scripts\poscore\validate-poscore-wpf-shell.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -TerminalId "AUTO"
```

Expected:

```text
PosCore WPF self-test started.
WPF shell initialized.
Local login view model ready:
Terminal status view model ready:
Sales view model ready:
Sync status view model ready:
Cash shift view model ready:
PosCore WPF shell validation completed.
message : PosCore WPF shell MVVM foundation completed.
```
