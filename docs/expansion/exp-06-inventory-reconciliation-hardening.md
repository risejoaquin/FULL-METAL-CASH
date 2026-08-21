# SolidPOS EXP-06 — Inventory Reconciliation Hardening

## Status

PENDING USER VALIDATION.

## Objective

EXP-06 hardens inventory reconciliation after EXP-05 exposed the live operating condition `negative_inventory_requires_reconciliation`.

The phase validates and reconciles:

- negative inventory by store/product/unit from the append-only inventory ledger;
- ledger vs projected stock consistency;
- controlled inventory_count and inventory_ledger adjustment evidence;
- modifier inventory semantics, including `substitute` behavior and `replaces_product_id`;
- recipe/BOM shape for active recipes and recipe items;
- low stock threshold coverage;
- GO/NO-GO criteria for EXP-07.

## Production behavior

This phase may write controlled reconciliation evidence in production when negative inventory exists:

- one `inventory_counts` row per affected store;
- one `inventory_count_lines` row per negative stock key;
- one compensating `inventory_ledger` row per negative stock key;
- movement type `adjustment`;
- reference type `inventory_count`;
- metadata tagged with `EXP-06` and `inventory_reconciliation_hardening`.

The ledger remains append-only. No existing inventory ledger row is updated or deleted.

## Hardening rules

EXP-06 does not use fragile assumptions that previously blocked expansion validators:

- no `pos.inventory_current` dependency;
- no `pos.inventory_ledger.sale_id` dependency;
- no `audit_events.created_at` dependency;
- no UUID/text comparison without explicit casts where needed.

## Gate

EXP-06 passes only if:

- build passes;
- tests pass;
- secret scan passes;
- health/readiness pass;
- admin login passes;
- monitoring endpoints are reachable;
- pending conflicts are zero;
- negative inventory after reconciliation projection is zero;
- modifier inventory semantics are valid;
- substitute modifiers have `replaces_product_id`;
- active recipe items are valid;
- schema version remains 4.

## Next phase

EXP-07 — Sync SLA and Offline Reliability Hardening.
