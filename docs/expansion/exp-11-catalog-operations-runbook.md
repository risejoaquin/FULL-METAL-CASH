# EXP-11 Catalog Operations Runbook

## Catalog operations

Catalog operations must be performed through protected admin endpoints, not direct SQL in normal support flow.

## Entities

- category
- product
- variant
- barcode
- modifier group
- modifier

## Operator rules

- Create a category before attaching a new product to it.
- Use unique SKUs and barcodes.
- Keep controlled validation products clearly prefixed with `EXP-11`.
- Use non-stock-tracked products for validation so inventory is not affected.
- Verify every catalog change in the runtime tenant catalog snapshot.

## No direct mutation rule

Operators must not directly update catalog rows in production unless following an approved incident or rollback runbook.
