# SolidPOS GA-04 — Production Data Integrity and Financial Reconciliation Gate

Status: `PASS REAL PRODUCTION`

GA-04 is the pre-release-candidate production data integrity gate. It does not repair commercial data automatically. All material mismatch counts must be zero before GA-05 is unlocked.

## Immutable contracts
- `schemaVersion = 4`
- `syncContract = schema_version_4`
- `generalAvailabilityActivated = False`
- `inventory_ledger` remains authoritative.
- modifier semantics remain `none | add | substitute`.
- known historical dead-letter evidence remains append-only and must retain `close_as_historical_evidence` audit evidence.

## Gate domains
Sales/payments, receipts, returns/refunds, cash, inventory, recipes/modifiers, catalog/pricing, users/access, sync/audit.

## Safety
No DELETE, TRUNCATE, ledger rewriting, forced cash-shift closure, sale/payment mutation, or refund mutation is part of GA-04. A non-zero material mismatch blocks the phase and requires explicit diagnosis/hotfix.

## Required result
`PASS GA PRODUCTION DATA INTEGRITY FINANCIAL RECONCILIATION / GO GA-05`
