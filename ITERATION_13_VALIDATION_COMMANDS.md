# Iteration 13 Validation Commands

## Restore

```powershell
dotnet restore solidpos-platform.sln
```

## Build

```powershell
dotnet build solidpos-platform.sln
```

## Tests

```powershell
dotnet test solidpos-platform.sln
```

## PosCore hardware abstraction E2E

```powershell
.\scripts\poscore\validate-poscore-hardware-abstraction.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -TerminalId "AUTO" `
  -TerminalToken "AUTO"
```

## Expected result

```text
Receipt print job queued.
Receipt print job processed.
Cash drawer command executed.
Barcode scanned.
Payment terminal authorization completed.
Local hardware status.
PosCore hardware abstraction runtime completed.
```
