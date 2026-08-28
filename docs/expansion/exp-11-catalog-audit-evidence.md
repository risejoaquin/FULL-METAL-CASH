# EXP-11 Catalog Audit Evidence

## Required audit evidence

EXP-11 expects audit_events evidence for:

- admin.catalog.category.upsert
- admin.catalog.product.upsert
- admin.catalog.variant.upsert
- admin.catalog.barcode.upsert
- admin.catalog.price.upsert
- admin.catalog.modifier_group.upsert
- admin.catalog.modifier.upsert

## Required sync evidence

EXP-11 expects sync_changes evidence for catalog and price changes:

- tenant.catalog
- price.updated

## Database contract

Audit must be stored in `pos.audit_events` and sync replay evidence in `pos.sync_changes`.

## NO-GO cases

NO-GO if audit_events are missing, sync_changes are missing, product price is negative, price window is invalid, tax mode is invalid, or modifier inventory behavior violates the closed modifier contract.
