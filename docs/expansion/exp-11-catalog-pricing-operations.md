# EXP-11 Catalog Pricing Operations

## Status

PENDING USER VALIDATION.

## Objective

EXP-11 closes controlled catalog pricing operations for limited production expansion. The phase validates that an operator with catalog permissions can manage category, product, variant, barcode, price, modifier group, and modifier records through the protected API, and that those changes are visible in the runtime tenant catalog snapshot.

## Scope

- catalog pricing operations
- category upsert
- product upsert
- variant upsert
- barcode upsert
- product price upsert
- modifier group upsert
- modifier upsert
- runtime catalog snapshot verification
- sync_changes evidence
- audit_events evidence
- SQL cross-check
- GO/NO-GO decision for EXP-12

## Safety contract

EXP-11 creates only controlled EXP-11 catalog evidence. It does not mutate historical sales, payments, cash drawer, inventory ledger, stores, terminals, sync inbox/outbox, customers, users, or release channels.

## Next phase

If EXP-11 passes, the next phase is EXP-12 Commercial Beta Readiness.


## HOTFIX 11.1 Controlled Price List Bootstrap

EXP-11 now ensures a tenant-scoped active MXN price list before product price validation. The bootstrap is idempotent, reuses an existing active MXN price list when available, otherwise creates `EXP11-MXN`. It does not mutate sales, payments, cash drawer, inventory ledger, stores, terminals, sync, customers, users, release channels, or tenant identity.
