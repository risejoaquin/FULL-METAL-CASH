# SolidPOS Iteration 16 — Validation Commands

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
.\scripts\poscore\validate-poscore-local-resilience.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -TerminalId "AUTO" `
  -TerminalToken "AUTO"
```

Expected result:

```text
Local integrity check.
Local database backup created.
Local resilience validation fixture created.
Local runtime recovery completed.
Local recovery journal.
message = PosCore local resilience/recovery/data integrity completed.
```
