# EXP-11 Pricing Policy

## Pricing policy

Catalog prices are attached to a price list. Every sellable product used by POS must have an active price in the correct currency.

## Required controls

- price list must exist and be active
- currency must be MXN for this tenant validation
- price must be non-negative
- price window must be valid
- `startsAt` and `endsAt` must not create an expired or inverted price window
- only active, non-deleted product prices should appear in runtime catalog

## Price window rule

A price is active only when `starts_at` is null or in the past, and `ends_at` is null or in the future.

## Historical sales rule

Changing catalog price must not mutate historical sales totals. Sales store their own line price snapshots.


## HOTFIX 11.1 Controlled Price List Bootstrap

EXP-11 now ensures a tenant-scoped active MXN price list before product price validation. The bootstrap is idempotent, reuses an existing active MXN price list when available, otherwise creates `EXP11-MXN`. It does not mutate sales, payments, cash drawer, inventory ledger, stores, terminals, sync, customers, users, release channels, or tenant identity.

## HOTFIX 11.2 runtime catalog visibility

The active price list is validated through SQL bootstrap and SQL cross-check. Runtime catalog visibility for `priceLists` is useful but not mandatory because the current `/api/v1/tenant/catalog` payload may omit price list metadata while still accepting valid price operations.
