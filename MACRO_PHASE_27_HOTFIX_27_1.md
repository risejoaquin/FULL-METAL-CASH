# Macro Fase 27 Hotfix 27.1

## Fix

`apply-postgresql-migrations.ps1` skipped `database/postgresql/011_discounts_promotions_runtime.sql` when an existing `pos` schema was detected.

That caused runtime code to execute against a database where `discounts.store_id`, `discounts.category_id`, and `discounts.product_id` did not exist.

## Change

Added `database/postgresql/011_discounts_promotions_runtime.sql` to the migration list for both fresh and existing schema paths.

## Scope

No API contracts, business rules, repository logic, or endpoints changed.
