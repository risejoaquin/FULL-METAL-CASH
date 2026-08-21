# EXP-01 Post-Pilot Changelog

Status: PENDING USER VALIDATION

## Pilot validation history

| Phase | Result | Production scope |
| --- | --- | --- |
| PILOT-01 | PASS REAL PRODUCTION / GO | Controlled store setup |
| PILOT-02 | PASS REAL PRODUCTION / GO | Real POS transaction validation |
| PILOT-03 | PASS REAL PRODUCTION / GO | Cash drawer and shift operations |
| PILOT-04 | PASS REAL PRODUCTION / GO | Receipts, returns and refunds |
| PILOT-05 | PASS REAL PRODUCTION / GO | Offline mode field test |
| PILOT-06 | PASS REAL PRODUCTION / GO | Sync recovery and conflict field test |
| PILOT-07 | PASS REAL PRODUCTION / GO | Dashboard operations monitoring |
| PILOT-08 | PASS REAL PRODUCTION / GO | Backup, restore and rollback drill |
| PILOT-09 | PASS REAL PRODUCTION / GO | Pilot incident runbook validation |
| PILOT-10 | PASS REAL PRODUCTION / GO | Pilot closure report and production expansion decision |

## Key production capabilities frozen

- Tenant/store production operation.
- Real POS sales.
- Cash payments and change.
- Cash shifts and drawer movements.
- Digital receipts.
- Returns and refunds.
- Offline sale creation.
- Offline-to-online sync.
- Sync recovery, retry and conflict handling.
- Dashboard monitoring.
- Backup/restore/rollback drill.
- Incident runbook.
- Limited production expansion decision.

## Residual monitored conditions

- retry_pending sync item requires monitoring.
- known dead_letter sync item requires triage.
- negative inventory item requires reconciliation.

## Next change category

The next changes must belong to EXP phases. Avoid large product features until EXP-01 and EXP-02 are closed.
