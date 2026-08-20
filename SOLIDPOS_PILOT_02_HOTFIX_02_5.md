# SolidPOS PILOT-02 Hotfix 02.5 — Sales Read Model Contract Shape

## Status
PENDING USER VALIDATION

## Scope
This hotfix hardens the PILOT-02 validation script after production showed that the sales list read model may not expose the created sale through the same property/envelope shape expected by the first script version.

## Root cause
The script only matched list items by `id` and only unwrapped `items`. Historical API outputs for SolidPOS sales read models may expose identifiers as `saleId` or `sale_id`, or wrap payloads under `data`, `sales`, or `results`. That created a false negative even after sale creation and sale detail validation passed.

## Fix
- Added `Get-SaleReadModelId` helper.
- Supports `id`, `saleId`, and `sale_id`.
- Supports response envelopes `items`, `data`, `sales`, and `results`.
- Keeps the existing multi-query list validation.
- Adds a fallback to the canonical `GET /api/v1/sales/{saleId}` read model before failing.

## Modules affected
- `scripts/pilot/validate-real-pos-transaction.ps1`

## Modules not changed
- Backend/API
- Database schema
- Dashboard
- POS sale logic
- Payment logic
- Receipt logic
- Cash drawer logic

## Expected result
PILOT-02 should continue past `Validating sale detail and read model PASS` and proceed to receipt, cash shift close, audit, and SQL persistence validation.
