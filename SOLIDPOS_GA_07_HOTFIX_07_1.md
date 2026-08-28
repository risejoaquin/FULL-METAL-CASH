# SolidPOS GA-07 Hotfix 07.1 — Canonical Receipt/Refund Table Contract

## Cause
GA-07 referenced non-existent aliases `pos.receipts` and `pos.refunds`. The canonical production tables are `pos.digital_receipts` and `pos.return_refunds`.

## Fix
- `ga-07-source-and-final-check.sql`: use canonical tables.
- GA-07 isolated restore reconciliation query: use the same canonical tables.
- No production data mutation.
- No migration, API, runtime, schemaVersion, or sync-contract change.

## Audit
The remaining GA-07 table references were checked against PostgreSQL migrations. No other non-canonical table names were found in the GA-07 source/final snapshot or restore reconciliation path.
