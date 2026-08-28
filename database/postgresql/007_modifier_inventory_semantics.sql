SET search_path TO pos, public;

ALTER TABLE modifiers
  ADD COLUMN IF NOT EXISTS inventory_behavior text NOT NULL DEFAULT 'none',
  ADD COLUMN IF NOT EXISTS consumption_quantity numeric(18,4),
  ADD COLUMN IF NOT EXISTS consumption_unit_id uuid REFERENCES units(id),
  ADD COLUMN IF NOT EXISTS replaces_product_id uuid REFERENCES products(id),
  ADD COLUMN IF NOT EXISTS replaces_variant_id uuid REFERENCES product_variants(id);

UPDATE modifiers m
SET inventory_behavior = 'add',
    consumption_quantity = COALESCE(m.consumption_quantity, 1),
    consumption_unit_id = COALESCE(m.consumption_unit_id, p.inventory_unit_id)
FROM products p
WHERE m.tenant_id = p.tenant_id
  AND m.linked_product_id = p.id
  AND m.linked_product_id IS NOT NULL
  AND p.is_stock_tracked = true
  AND p.inventory_unit_id IS NOT NULL
  AND m.inventory_behavior = 'none';

ALTER TABLE modifiers DROP CONSTRAINT IF EXISTS modifiers_inventory_behavior_check;
ALTER TABLE modifiers
  ADD CONSTRAINT modifiers_inventory_behavior_check
  CHECK (inventory_behavior IN ('none', 'add', 'substitute'));

ALTER TABLE modifiers DROP CONSTRAINT IF EXISTS modifiers_inventory_effect_shape_check;
ALTER TABLE modifiers
  ADD CONSTRAINT modifiers_inventory_effect_shape_check
  CHECK (
    inventory_behavior = 'none'
    OR (
      linked_product_id IS NOT NULL
      AND consumption_quantity IS NOT NULL
      AND consumption_quantity > 0
      AND consumption_unit_id IS NOT NULL
      AND (
        inventory_behavior <> 'substitute'
        OR replaces_product_id IS NOT NULL
      )
    )
  );

CREATE INDEX IF NOT EXISTS idx_modifiers_inventory_effect
  ON modifiers (tenant_id, inventory_behavior, linked_product_id)
  WHERE deleted_at IS NULL AND inventory_behavior <> 'none';


-- Development seed correction for existing local databases that already had linked milk modifiers.
-- The default milk option represents the recipe's base ingredient, so selecting it must not add
-- an extra 1 ml movement. Oat milk is a substitution of the recipe's 250 ml whole-milk component.
UPDATE modifiers
SET inventory_behavior = 'none',
    consumption_quantity = NULL,
    consumption_unit_id = NULL,
    replaces_product_id = NULL,
    replaces_variant_id = NULL,
    updated_at = now()
WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
  AND id = '51000000-0000-0000-0000-000000000001';

UPDATE modifiers
SET linked_product_id = '30000000-0000-0000-0000-000000000006',
    linked_variant_id = NULL,
    inventory_behavior = 'substitute',
    consumption_quantity = 250,
    consumption_unit_id = '11000000-0000-0000-0000-000000000003',
    replaces_product_id = '30000000-0000-0000-0000-000000000005',
    replaces_variant_id = NULL,
    updated_at = now()
WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
  AND id = '51000000-0000-0000-0000-000000000002';
