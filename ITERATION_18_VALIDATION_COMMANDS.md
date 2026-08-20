# SolidPOS Iteration 18 — Validation Commands

```powershell
dotnet restore solidpos-platform.sln
```

```powershell
dotnet build solidpos-platform.sln
```

```powershell
dotnet test solidpos-platform.sln
```

```powershell
.\scripts\poscore\validate-posbuilder-update-packaging.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -TenantName "Mi Cafeteria" `
  -AppName "Mi Cafeteria POS" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -TerminalId "AUTO" `
  -ReleaseVersion "1.0.0" `
  -Channel "stable"
```

Expected final message:

```text
PosBuilder updates real packaging completed.
```
