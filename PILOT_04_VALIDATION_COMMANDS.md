# SolidPOS PILOT-04 — Validation Commands

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

## 4. DATABASE_URL

```powershell
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```

Expected:

```text
postgresql://
```

## 5. Admin password

```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"
```

## 6. Run PILOT-04

```powershell
.\scripts\pilot\validate-receipts-returns-refunds.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -StoreCode "MAIN" `
  -ProductSku "QSR-AMERICANO" `
  -PaymentMethodCode "cash"
```

## Expected final result

```text
[PILOT-04] Validating receipts/returns/refunds persistence via PostgreSQL PASS
[PILOT-04] Pilot receipts/returns/refunds log initialized PASS

goNoGo : GO
message : SolidPOS PILOT-04 receipts returns refunds validation completed.
```

## Logs to send if failure

- Full PowerShell output from first failing `[PILOT-04]` step.
- If failure occurs after sale creation: `saleId` and `saleLineId`.
- If failure occurs after receipt issue: `receiptId`, `receiptNumber`, and `publicToken` only if needed for debugging; do not publish tokens externally.
- If failure occurs after return creation: `returnId`, `saleId`, and SQL output.


## Hotfix 04.1

El endpoint de email receipt puede devolver `queued_stub`. El validador ahora acepta `queued`, `queued_stub`, `stub_queued` y `sent`.
