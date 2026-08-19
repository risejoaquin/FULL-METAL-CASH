# Iteration 04 Validation Commands

## 1. Build and tests

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```

Expected:

```text
Build correcto
PosServer tests PASS
PosCore tests PASS
```

## 2. Obtain terminal binding values

Use the terminal created by the Iteration 02/03 scripts, or create/enroll a new terminal through the existing terminal enrollment flow.

Required values:

```text
TenantId
StoreId
TerminalId
TerminalToken
ProductId
```

Known product from Iteration 02 seed:

```text
QSR-AMERICANO / Americano 12oz
```

## 3. Validate local SQLite runtime

```powershell
.\scripts\poscore\validate-poscore-local-runtime.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -TerminalId "TERMINAL_ID" `
  -TerminalToken "TERMINAL_TOKEN" `
  -ProductId "dd272b64-d450-4dd5-ace2-b17fc04ecc62"
```

Expected:

```text
Initializing local PosCore SQLite runtime...
Binding local terminal...
Creating offline sale and local outbox event...
Checking local outbox...
PosCore local SQLite runtime validation completed.
```

## 4. Git workflow

```powershell
git add .

git commit -m "Iteration 04 PosCore local SQLite offline runtime foundation"

git push
```
