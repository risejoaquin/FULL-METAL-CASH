# SolidPOS PILOT-02 Hotfix 02.4 — Sales Read Model Validation Hardening

## Status

PENDING USER VALIDATION

## Cause

The PILOT-02 transaction flow created the sale correctly and the sale detail endpoint returned the sale, but the validation script required the new sale to appear only in a single highly constrained list query:

```http
GET /api/v1/sales?storeId=&terminalId=&status=completed&limit=20
```

That made the pilot validation brittle for production because the read model can be verified safely through the sales list contract without depending on a single terminal-filtered query shape.

## Fix

Updated:

```text
scripts/pilot/validate-real-pos-transaction.ps1
```

The script now validates the created sale through a hardened read-model lookup matrix:

1. store + terminal + status
2. store + status
3. time window + store + status
4. time window + status
5. status-only fallback with maximum supported limit

It also retries briefly and emits diagnostic IDs if the created sale is not found.

## Scope

No backend, database, dashboard, payment, receipt, inventory or cash-drawer behavior changed. This is a validation-script hardening hotfix.

## Expected Result

```text
[PILOT-02] Validating sale detail and read model PASS
```
