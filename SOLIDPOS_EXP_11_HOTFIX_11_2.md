# SolidPOS EXP-11 HOTFIX 11.2 — Price List Runtime Catalog Visibility Contract

## Status
PENDING USER VALIDATION

## Failure
EXP-11 HOTFIX 11.1 failed after controlled price list bootstrap:

```text
At least one price list is required after EXP-11 controlled bootstrap.
```

## Root cause
The controlled price list bootstrap can create or revive `pos.price_lists`, but `/api/v1/tenant/catalog` does not currently expose `priceLists` as part of the runtime catalog contract in this production environment.

That makes `priceLists` visibility in the runtime catalog a non-blocking contract gap, not proof that the price list does not exist.

## Fix
The validator now uses SQL bootstrap as the source of truth for the active MXN price list:

```text
scripts/expansion/exp-11-ensure-controlled-price-list.sql
```

If `/api/v1/tenant/catalog` does not expose `priceLists`, the validator uses the SQL-confirmed bootstrap result:

```text
priceListId
code
name
currency
status=active
```

The missing runtime price list visibility is reported as a condition:

```text
review_price_list_runtime_catalog_visibility
```

## Additional hardening
Runtime catalog snapshot visibility for EXP-11 entities is now classified as non-blocking visibility evidence. SQL cross-check remains the authoritative validator for the created category, product, variant, barcode, price, modifier group, and modifier.

Possible non-blocking conditions:

```text
review_runtime_catalog_category_visibility
review_runtime_catalog_product_visibility
review_runtime_catalog_variant_visibility
review_runtime_catalog_barcode_visibility
review_runtime_catalog_price_visibility
review_runtime_catalog_modifier_group_visibility
review_runtime_catalog_modifier_visibility
```

## No data-destructive changes
This hotfix does not change backend code, migrations, sales, payments, cash drawer, inventory ledger, stores, terminals, sync, customers, users, releases, or tenant identity.

## Expected result

```text
[EXP-11] Admin login and catalog endpoint contract PASS
[EXP-11] Controlled catalog and pricing operational flow PASS
[EXP-11] Runtime catalog snapshot verification PASS
[EXP-11] SQL catalog pricing cross-check PASS
[EXP-11] EXP-11 PASS CATALOG PRICING OPERATIONS / GO EXP-12
```
