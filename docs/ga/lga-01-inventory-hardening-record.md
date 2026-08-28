# LGA-01 Inventory Hardening Record

## Known item

SKU: ING-CAFE-G.

Issue: negative stock observed after controlled PosCore Americano sales.

## Decision

Default decision: OBSERVE.

If `-ApplyInventoryAdjustment` is used, the validator can apply a controlled inventory adjustment to bring negative stock back to zero using the inventory adjustment endpoint.

## Guardrail

The validator blocks if negative stock exceeds the allowed baseline. Default allowed negative stock item count: 1.

## Public GA impact

Public GA remains not activated until negative stock is either remediated or accepted through a separate explicit operational decision.
