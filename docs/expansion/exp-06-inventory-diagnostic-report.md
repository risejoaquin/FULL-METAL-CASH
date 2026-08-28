# EXP-06 Inventory Diagnostic Report

## Purpose

This report defines how SolidPOS diagnoses negative inventory and ledger vs stock differences before advancing to sync SLA hardening.

## Evidence sources

- `pos.inventory_ledger` is the source of truth.
- Stock is derived from ledger aggregation by tenant, store, product, variant and unit.
- Negative inventory is any derived stock row where `quantity_on_hand < 0`.
- `pos.inventory_counts` and `pos.inventory_count_lines` provide reconciliation evidence.
- `pos.audit_events` provides operational trace evidence through `occurred_at`.

## Root cause categories

Negative inventory must be classified into one of these categories:

- controlled sale consumption before purchase/stock receipt;
- offline sale accepted and later synchronized;
- recipe/BOM consumption greater than current stock;
- modifier substitution or add-on consumption;
- missing initial stock count;
- manual adjustment required.

## Required evidence

A valid diagnostic must include:

- store id;
- product id;
- unit id;
- previous quantity;
- counted quantity;
- adjustment delta;
- reference id;
- source event id;
- ledger movement created by reconciliation.

## Output

EXP-06 writes a manifest with negative inventory before count, adjustment totals and projected after count.
