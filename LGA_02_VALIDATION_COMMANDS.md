# LGA-02 Validation Commands

## 1. Open WPF and confirm visual command enablement

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

dotnet run --project .\src\PosCore\SolidPOS.PosCore.Wpf\SolidPOS.PosCore.Wpf.csproj
```

Confirm visually that these buttons enable when the cart/tender state allows it:

- Cobrar efectivo
- Encolar recibo fake
- Actualizar sync visual

## 2. Repeat real sales cycle

Create at least 3 new controlled sales and 3 payments in Limited GA before running the validator. Use WPF if the real sale flow is ready; otherwise use the already validated PosCore CLI flow.

## 3. Run LGA-02 validator

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-lga-02-limited-ga-stability-loop-operational-cleanup.ps1
Unblock-File .\scripts\ga\validate-lga-01-limited-ga-operations-hardening.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
Unblock-File .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1

Select-String .\scripts\ga\validate-lga-02-limited-ga-stability-loop-operational-cleanup.ps1 -Pattern "LGA-02.3-inventory-adjustment-contract-alignment"

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString

if ([string]::IsNullOrWhiteSpace($env:DATABASE_URL)) {
  throw "DATABASE_URL no está configurado en esta sesión."
} else {
  "DATABASE_URL presente en sesión: OK"
}

.\scripts\ga\validate-lga-02-limited-ga-stability-loop-operational-cleanup.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ConflictDecision FORMAL_ARCHIVE `
  -DeadLetterDecision FORMAL_ARCHIVE `
  -InventoryDecision ADJUSTED `
  -MaxStores 2 `
  -MaxConcurrentTerminals 2 `
  -BaselineCompletedSalesInLast24h 3 `
  -BaselinePaymentsInLast24h 3 `
  -MinimumNewCompletedSales 3 `
  -MinimumNewPayments 3 `
  -AllowedExistingSyncConflictCount 3 `
  -AllowedDeadLetterCount 1 `
  -AllowedNegativeStockItemCount 0 `
  -AllowedWaitingConnectionCount 11 `
  -ApplyInventoryAdjustment `
  -WpfVisualConfirmed `
  -SkipDashboardBuild `
  -SkipLga01Revalidation
```

Expected final line:

```text
[LGA-02] PASS LGA-02 LIMITED GA STABILITY LOOP AND OPERATIONAL CLEANUP / GO LGA-03
```


## LGA-02-HOTFIX-01 — Real sales cycle document contract alignment

Aligned `docs/ga/lga-02-real-sales-cycle-record.md` with validator guardrails by explicitly including the required `stability loop` term. No backend, WPF, database migration, or Public GA activation changes.


## LGA-02-HOTFIX-03 — Inventory Adjustment Contract Alignment

Aligned the validator inventory adjustment payload with the production API by using `adjustmentType = correction` and invariant `quantityDelta`. Public GA remains NOT_ACTIVATED.
