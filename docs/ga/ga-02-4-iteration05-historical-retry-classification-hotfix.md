# HOTFIX GA-02.4 — Iteration 05 historical retry classification

## Cause
GA-02 correctly refused automatic remediation because a retry row had `validationFixture=false`. Production evidence showed the row belongs to the documented Iteration 05 E2E PosCore sync terminal: `iteration-05-poscore-sync-<tenantId>`.

## Fix
GA-02 now recognizes only the documented Iteration 05 historical E2E retry signature when all of these are true:

- fingerprint is exactly `iteration-05-poscore-sync-<tenantId>`;
- event is `sale.completed` / entity `sale`;
- status is `retry_pending`;
- event predates the fresh GA-01 baseline;
- error code is `processing_exception`;
- error message matches the historical `CreateSaleLineRequest` JSON conversion failure.

This is intentionally narrower than a generic `iteration-*` rule. Commercial or ambiguous sale retries remain non-remediable and block GA-02.

## Safety
No DELETE/TRUNCATE is used. The safe GA-02 remediation changes only the historical validation retry from executable `retry_pending` to retained historical `rejected` evidence and writes append-only audit evidence. The PILOT-06 dead-letter remains physically retained and only receives an explicit historical-evidence audit decision.

## Status
PENDING USER VALIDATION.
