# EXP-11 GO/NO-GO

## GO criteria

EXP-11 can move to EXP-12 when:

- catalog document contract passes
- build passes
- tests pass
- production health/readiness passes
- admin login passes
- catalog endpoint contract passes
- category/product/variant/barcode/price/modifier operations pass
- runtime catalog snapshot includes controlled records
- SQL catalog pricing cross-check passes
- audit_events and sync_changes evidence pass

## NO-GO criteria

NO-GO if:

- protected admin catalog endpoints fail
- product is not visible in runtime catalog
- active price is missing
- negative price exists
- invalid price window exists
- invalid tax mode exists
- invalid modifier behavior exists
- audit or sync evidence is missing

## Next phase

GO authorizes EXP-12 Commercial Beta Readiness.


## HOTFIX 11.1 Controlled Price List Bootstrap

EXP-11 now ensures a tenant-scoped active MXN price list before product price validation. The bootstrap is idempotent, reuses an existing active MXN price list when available, otherwise creates `EXP11-MXN`. It does not mutate sales, payments, cash drawer, inventory ledger, stores, terminals, sync, customers, users, release channels, or tenant identity.

## HOTFIX 11.2 GO rule

EXP-11 may be GO when `pos.price_lists` and `pos.product_prices` pass SQL cross-check even if `/api/v1/tenant/catalog` does not expose `priceLists`. In that case add `review_price_list_runtime_catalog_visibility` as a non-blocking condition.
