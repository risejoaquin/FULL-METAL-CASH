# Macro Phase 09 Status - Inventory Ledger Sale Effects + BOM Consumption

## Goal

Every completed sale must create append-only inventory effects in PostgreSQL.

The POS remains operational even when stock becomes negative; negative quantities are visible through `inventory_stock` and can be audited later.

## Implemented

- Sale inventory side effects run inside the same PostgreSQL transaction as:
  - `sales`
  - `sale_lines`
  - `payments`
  - cash expected amount update
- Idempotency is preserved by returning the existing sale before inserting new inventory ledger rows.
- Direct stock deduction for simple tracked products.
- Recipe/BOM consumption for prepared products.
- Modifier-linked ingredient consumption.
- Append-only writes to `inventory_ledger`.
- Stock report endpoint:
  - `GET /api/v1/inventory/stock`
- `inventory_stock` is used as the stock read model.
- Terminal stock reads are automatically scoped to the terminal store.
- User/admin stock reads can query all tenant stores or filter by `storeId`.
- OpenAPI contract updated for `InventoryStockItem`.
- Unit tests for stock contract and terminal store isolation.

## Sale Effects

| Sale line type | Ledger movement type | Quantity behavior |
| --- | --- | --- |
| Simple tracked product | `sale` | `-sale_quantity` |
| Recipe/BOM product | `sale_recipe_component` | `-(recipe_item_quantity * sale_quantity * waste_multiplier)` |
| Modifier linked to ingredient | `sale_recipe_component` | `-sale_quantity` |

## Current Limits

- Modifier consumption quantity is still schema-limited. Current base behavior consumes one linked ingredient unit per sold line quantity.
- Modifier substitution rules are not modeled yet. A later schema phase should add `consumption_quantity`, `consumption_unit_id`, and `behavior = add | substitute`.
- Negative stock is intentionally allowed and exposed for audit; no sale is blocked by inventory availability in this phase.
- Initial stock seed is not part of this phase, so demo stock values may start negative after sales.

## Smoke Test

After registering a terminal, opening a shift and creating a sale:

```powershell
$stock = Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:5000/api/v1/inventory/stock" `
  -Headers @{ Authorization = "Bearer $($terminalSession.accessToken)" }

$stock | Select-Object productId,variantId,unitId,quantityOnHand
```

For the demo Latte sale, expect rows for recipe ingredients such as coffee, milk and cup. Values may be negative because the MVP explicitly allows negative stock with audit.
