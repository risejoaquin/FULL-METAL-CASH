# SolidPOS Iteration 17 — Validation Commands

## 1. Restore

```powershell
dotnet restore solidpos-platform.sln
```

## 2. Build

```powershell
dotnet build solidpos-platform.sln
```

Expected:

```text
Compilación correcta.
0 Advertencia(s)
0 Errores
```

## 3. Tests

```powershell
dotnet test solidpos-platform.sln
```

Expected:

```text
SolidPOS.PosCore.UnitTests PASS
SolidPOS.PosServer.UnitTests PASS
SolidPOS.PosServer.IntegrationTests PASS
SolidPOS.PosServer.ContractTests PASS
```

## 4. PosBuilder / Branding Package / PosCore WPF Consumption

```powershell
.\scripts\poscore\validate-posbuilder-branding-package.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -TenantName "Mi Cafeteria" `
  -AppName "Mi Cafeteria POS" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -TerminalId "AUTO"
```

Expected:

```text
Tenant branding package created.
Tenant branding package validation. isValid=True
PosBuilder branding self-test started.
PosBuilder tenant branding package validation completed.
PosCore WPF QSR self-test started.
Branding package applied: tenantName=Mi Cafeteria; appName=Mi Cafeteria POS
Receipt branding ready: header=Mi Cafeteria; footer=Gracias por su compra.
PosCore WPF sales flow QSR validation completed.
message : PosBuilder tenant branding package foundation completed.
```

## Stop rule

If restore/build/test fails, do not continue with the script. Send the full failing log.
