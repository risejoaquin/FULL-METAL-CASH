# SOLIDPOS EXP-11 HOTFIX 11.1 — Controlled Price List Bootstrap Contract

## Status
PENDING USER VALIDATION

## Failure
EXP-11 failed during the catalog endpoint contract with:

```text
At least one price list is required.
```

## Root cause
The production tenant can expose a valid catalog runtime with units and catalog entities while having no active `pos.price_lists` row visible to `/api/v1/tenant/catalog`. EXP-11 needs a valid active MXN price list before it can create product prices.

## Fix
The validator now runs a controlled, idempotent price-list bootstrap before reading the runtime catalog snapshot.

The bootstrap:

- reuses an existing active MXN price list when available;
- otherwise creates `EXP11-MXN` as an active MXN price list;
- is tenant-scoped;
- is idempotent;
- does not touch sales, payments, cash drawer, inventory ledger, stores, terminals, sync inbox/outbox, customers, users, or release channels.

## Files changed

```text
scripts/expansion/validate-exp-11-catalog-pricing-operations.ps1
scripts/expansion/exp-11-ensure-controlled-price-list.sql
SOLIDPOS_EXP_11_HOTFIX_11_1.md
SOLIDPOS_EXP_11_CATALOG_PRICING_OPERATIONS.md
EXP_11_VALIDATION_COMMANDS.md
```

## Expected result

```text
[EXP-11] Admin login and catalog endpoint contract PASS
[EXP-11] Controlled catalog and pricing operational flow PASS
[EXP-11] EXP-11 PASS CATALOG PRICING OPERATIONS / GO EXP-12
```
