# EXP-04 Second Store Rollback Runbook

## Purpose
Contain a failed second store limited expansion without affecting MAIN store operations.

## Rollback triggers
- Store creation succeeds but terminal registration fails.
- Terminal registration succeeds but bootstrap mismatches store or terminal.
- Cash shift cannot close with zero difference.
- Controlled sale appears under the wrong store.
- Dead letter or pending conflict appears for the new terminal.
- SQL cross-check returns NO-GO.

## Containment
- Stop use of the new terminal.
- Mark the terminal blocked/retired through admin controls when available.
- Keep the store active only if it is needed for investigation; otherwise mark inactive after approval.
- Preserve cash shift, sale, receipt, and audit evidence.
- Do not delete production rows.

## Evidence to keep
- storeId
- terminalId
- shiftId
- saleId
- receiptId
- PowerShell output
- second-store-expansion-manifest.json
- exp-04-second-store-limited-expansion-log.md
