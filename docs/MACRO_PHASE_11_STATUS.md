# Macro Phase 11 Status - Sales Void + Inventory/Cash Compensation

## Goal

Cancel a completed operational sale without deleting historical records.

The void operation must preserve auditability, compensate inventory through append-only ledger rows, mark payments as voided, and adjust expected cash when the original sale affected cash.

## Implemented

- `POST /api/v1/sales/{saleId}/void`.
- Protected by `sales.void`.
- Terminal runtime context required.
- `VoidSaleRequest` contract:
  - `voidedByUserId`
  - `reason`
  - `occurredAt`
- Sale row is updated from `completed` to `voided`.
- Sale `version` increments.
- Sale metadata stores void actor, reason and timestamp.
- Approved payments for the sale are marked `voided`.
- Original sale inventory effects are compensated with append-only `inventory_ledger` rows:
  - `movement_type = void_compensation`
  - `reference_type = sale_void`
  - `reference_id = original sale id`
  - `quantity_delta = -original sale quantity_delta`
- Cash compensation subtracts the original net cash impact from `cash_shifts.expected_cash_cents`.
- Idempotent behavior:
  - voiding an already `voided` sale returns the same sale response and does not insert another compensation.
- Unit tests for void contract and service behavior.
- OpenAPI contract updated.

## Rules

| Condition | Result |
| --- | --- |
| Sale belongs to same tenant/store/terminal | Can proceed |
| Sale status is `completed` | Can be voided |
| Sale status is `voided` | Returns existing voided sale |
| Sale shift is closed | Rejected in this MVP |
| Actor user is inactive/missing | Rejected |

## Current Limits

- Void requires the original cash shift to still be open. Closed-shift corrections will be a later manager/admin workflow.
- The request is idempotent by sale status, not by a separate `local_void_id` yet.
- Manager PIN approval is not modeled yet; `voidedByUserId` is validated as active user.
- Fiscal cancellation/SAT remains intentionally excluded.

## Smoke Test

Create a fresh sale with an open shift, then:

```powershell
$voidSaleBody = @{
  voidedByUserId = "33333333-3333-3333-3333-333333333333"
  reason = "Error de captura demo"
  occurredAt = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json

$voidedSale = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5000/api/v1/sales/$($sale.id)/void" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" } `
  -ContentType "application/json" `
  -Body $voidSaleBody

$voidedSale.status
```

Expected status: `voided`.

Repeat the same request and expect:

```powershell
$sameVoidAgain.id -eq $voidedSale.id
```

Expected: `True`.
