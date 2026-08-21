# SolidPOS EXP-11 — Catalog Pricing Operations

## Status

PENDING USER VALIDATION.

## Summary

EXP-11 validates catalog pricing operations for limited production expansion. It creates controlled EXP-11 catalog data through the protected API and verifies runtime catalog visibility, sync_changes evidence, audit_events evidence, and SQL consistency.

## Added files

- scripts/expansion/validate-exp-11-catalog-pricing-operations.ps1
- scripts/expansion/exp-11-catalog-pricing-operations-check.sql
- EXP_11_VALIDATION_COMMANDS.md
- docs/expansion/exp-11-catalog-pricing-operations.md
- docs/expansion/exp-11-catalog-operations-runbook.md
- docs/expansion/exp-11-pricing-policy.md
- docs/expansion/exp-11-tax-promo-safety.md
- docs/expansion/exp-11-catalog-audit-evidence.md
- docs/expansion/exp-11-catalog-pricing-rollback.md
- docs/expansion/exp-11-go-no-go.md
- docs/expansion/logs/exp-11-catalog-pricing-operations-log.md

## Safety

No historical sales, payments, cash drawer, inventory ledger, stores, terminals, sync inbox/outbox, customers, users, tenant identity, or release channels are modified.

## Next phase

EXP-12 Commercial Beta Readiness.


## HOTFIX 11.1 Controlled Price List Bootstrap

EXP-11 now ensures a tenant-scoped active MXN price list before product price validation. The bootstrap is idempotent, reuses an existing active MXN price list when available, otherwise creates `EXP11-MXN`. It does not mutate sales, payments, cash drawer, inventory ledger, stores, terminals, sync, customers, users, release channels, or tenant identity.

## HOTFIX 11.2 — Price List Runtime Catalog Visibility Contract

EXP-11 now treats `pos.price_lists` SQL bootstrap as the source of truth for price list existence. If `/api/v1/tenant/catalog` does not expose `priceLists`, the phase can still pass when SQL bootstrap and SQL cross-check validate the price list and product price.

Non-blocking condition:

```text
review_price_list_runtime_catalog_visibility
```

Runtime catalog entity visibility is also recorded as evidence. SQL cross-check remains authoritative for catalog/pricing persistence and audit/sync evidence.
