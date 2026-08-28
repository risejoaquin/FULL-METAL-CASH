# SolidPOS Iteration 15 — Validation Commands

## 1. Restore

```powershell
dotnet restore solidpos-platform.sln
```

## 2. Build

```powershell
dotnet build solidpos-platform.sln
```

## 3. Tests

```powershell
dotnet test solidpos-platform.sln
```

## 4. WPF QSR Sales Flow Self-Test

```powershell
.\scripts\poscore\validate-poscore-wpf-sales-flow-qsr.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -TerminalId "AUTO"
```

## Expected

```text
PosCore WPF QSR self-test started.
WPF shell initialized.
QSR cart ready:
Cash payment ready:
Receipt print flow ready:
Sync visual state ready:
Cash shift view model ready:
QSR totals: totalCents=4500; tenderedCents=5000; changeCents=500; expectedCashCents=14500
PosCore WPF sales flow QSR validation completed.
```
