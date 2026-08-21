# EXP-06 Inventory Reconciliation Rollback

## Rule

The inventory ledger is append-only. This is the explicit append-only ledger contract: rollback is performed through a compensating adjustment, not by updating or deleting existing rows.

## Rollback process

1. Locate `inventory_counts` rows with reason `EXP-06 inventory reconciliation hardening`.
2. Locate related `inventory_count_lines` and `inventory_ledger` rows.
3. Create a new inventory count with rollback reason.
4. Insert compensating `inventory_count_lines` using the negative of the previous adjustment delta.
5. Insert compensating `inventory_ledger` rows with `movement_type = adjustment`.
6. Validate derived stock again.

## Evidence required

- original `inventory_count` id;
- original ledger reference id;
- compensating inventory_count id;
- compensating adjustment delta;
- operator/user id;
- timestamp.


## Append-only ledger evidence

EXP-06 rollback must preserve the append-only ledger rule. Every rollback uses a new compensating adjustment row in `inventory_ledger` with `movement_type = adjustment` and `reference_type = inventory_count`; existing ledger rows are never updated or deleted.
