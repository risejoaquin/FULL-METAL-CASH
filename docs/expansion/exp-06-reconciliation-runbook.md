# EXP-06 Reconciliation Runbook

## Principle

Inventory reconciliation is append-only. Existing ledger rows are never edited or deleted.

## Process

1. Build derived stock from `inventory_ledger`.
2. Detect every negative stock key.
3. Create one `inventory_counts` row per affected store.
4. Create one `inventory_count_lines` row per affected stock key.
5. Insert one `inventory_ledger` adjustment per affected stock key.
6. Project stock after adjustment.
7. Pass only when projected negative inventory equals zero.

## Idempotent behavior

The validator is idempotent by effect:

- first run reconciles current negative stock;
- second run sees no negative inventory and inserts no new adjustment rows;
- if new negative inventory appears later, a new reconciliation count is created for the new condition.

## Tables

- `inventory_counts`
- `inventory_count_lines`
- `inventory_ledger`

## Adjustment contract

- `movement_type = adjustment`
- `reference_type = inventory_count`
- `reference_id = inventory_counts.id`
- `source_event_id = inventory_counts.local_count_id`
- `metadata.phase = EXP-06`
- `metadata.contract = inventory_reconciliation_hardening`
