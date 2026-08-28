# Macro Fase 27 — Discounts / Promotions Hardening

Status: IMPLEMENTED — pending local validation

## Objective

Add a controlled discounts engine for percentage and fixed-amount discounts with status, validity windows, product/category/store/tenant scope, pre-sale validation and report-safe financial semantics.

## Financial rule

```text
grossSales
- discounts
= netSales
+ taxes/tips
= totalSales
```

Returns/refunds remain separate financial events and continue to affect `netAfterReturnsCents`.

## Endpoints

```http
GET   /api/v1/discounts
POST  /api/v1/discounts
POST  /api/v1/discounts/validate
PATCH /api/v1/discounts/{discountId}
```

## Decisions

- Discounts are tenant-owned.
- Scope is optional and can be tenant-wide, store-specific, category-specific or product-specific.
- Only `percentage` and `fixed_amount` are enabled in this phase.
- Existing advanced discount types (`buy_x_get_y`, `combo_price`) are intentionally excluded until a dedicated promotions engine phase.
- Sales with `discountCents > 0` now require a valid `discountId` in the sale line.
- Sales with no discount remain backwards compatible.

## New permissions

```text
discounts.read
discounts.manage
discounts.validate
```

## Audit events

```text
discount.created
discount.updated
```

## Migration

```text
database/postgresql/011_discounts_promotions_runtime.sql
```
