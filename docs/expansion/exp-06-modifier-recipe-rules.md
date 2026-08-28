# EXP-06 Modifier and Recipe Inventory Rules

## Modifier rules

Allowed `inventory_behavior` values:

- `none`
- `add`
- `substitute`

For `add` and `substitute`, the modifier must have:

- `linked_product_id`
- `consumption_quantity > 0`
- `consumption_unit_id`

For `substitute`, the modifier must also have:

- `replaces_product_id`

This prevents double deduction of base ingredient and substitute ingredient.

## Recipe rules

Active recipes must have valid recipe items:

- `ingredient_product_id` is required;
- `quantity > 0`;
- `unit_id` is required;
- recipe item belongs to an active recipe through `recipe_id`.

## Validation

EXP-06 validates modifiers and recipes before GO/NO-GO. Invalid modifier or recipe semantics block the phase.
