\set ON_ERROR_STOP on

BEGIN;
SET search_path TO pos, public;
SELECT set_config('app.tenant_id', :'tenant_id', true);

-- Iteration 09 runtime seed: QSR-AMERICANO must have a real active recipe/BOM
-- so PosCore can cache inventory consumption and compare local estimated movements
-- against the remote inventory_ledger after sync processing.

WITH mass_family AS (
  INSERT INTO pos.unit_families (tenant_id, code, name)
  VALUES (:'tenant_id'::uuid, 'mass', 'Masa')
  ON CONFLICT (tenant_id, code) DO UPDATE SET name = EXCLUDED.name
  RETURNING id
), gram_unit AS (
  INSERT INTO pos.units (tenant_id, family_id, code, name, symbol, factor_to_base, is_base)
  SELECT :'tenant_id'::uuid, id, 'g', 'Gramo', 'g', 1, true
  FROM mass_family
  ON CONFLICT (tenant_id, code) DO UPDATE SET
    name = EXCLUDED.name,
    symbol = EXCLUDED.symbol,
    factor_to_base = EXCLUDED.factor_to_base,
    is_base = EXCLUDED.is_base
  RETURNING id
), ingredient AS (
  INSERT INTO pos.products (
    id,
    tenant_id,
    category_id,
    sku,
    name,
    description,
    product_type,
    sale_unit_id,
    inventory_unit_id,
    purchase_unit_id,
    is_sellable,
    is_stock_tracked,
    allow_negative_stock,
    tax_mode,
    status
  )
  SELECT
    '71000000-0000-0000-0000-000000000001'::uuid,
    :'tenant_id'::uuid,
    NULL,
    'ING-CAFE-G',
    'Café grano/molido',
    'Ingrediente operativo para consumo local/remoto de Americano 12oz.',
    'ingredient',
    NULL,
    gram_unit.id,
    gram_unit.id,
    false,
    true,
    true,
    'exempt',
    'active'
  FROM gram_unit
  ON CONFLICT (tenant_id, sku) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    product_type = 'ingredient',
    inventory_unit_id = EXCLUDED.inventory_unit_id,
    purchase_unit_id = EXCLUDED.purchase_unit_id,
    is_sellable = false,
    is_stock_tracked = true,
    allow_negative_stock = true,
    tax_mode = 'exempt',
    status = 'active',
    deleted_at = NULL,
    updated_at = now()
  RETURNING id
), output_product AS (
  SELECT id
  FROM pos.products
  WHERE tenant_id = :'tenant_id'::uuid
    AND sku = 'QSR-AMERICANO'
    AND deleted_at IS NULL
  LIMIT 1
), recipe AS (
  INSERT INTO pos.recipes (
    id,
    tenant_id,
    output_product_id,
    output_variant_id,
    version,
    yield_quantity,
    yield_unit_id,
    waste_percent,
    status
  )
  SELECT
    '71000000-0000-0000-0000-000000000002'::uuid,
    :'tenant_id'::uuid,
    output_product.id,
    NULL,
    1,
    1,
    gram_unit.id,
    0,
    'active'
  FROM output_product
  CROSS JOIN gram_unit
  ON CONFLICT (id) DO UPDATE SET
    output_product_id = EXCLUDED.output_product_id,
    output_variant_id = EXCLUDED.output_variant_id,
    version = EXCLUDED.version,
    yield_quantity = EXCLUDED.yield_quantity,
    yield_unit_id = EXCLUDED.yield_unit_id,
    waste_percent = EXCLUDED.waste_percent,
    status = 'active',
    deleted_at = NULL,
    updated_at = now()
  RETURNING id
)
INSERT INTO pos.recipe_items (
  id,
  tenant_id,
  recipe_id,
  ingredient_product_id,
  ingredient_variant_id,
  quantity,
  unit_id,
  optional
)
SELECT
  '71000000-0000-0000-0000-000000000003'::uuid,
  :'tenant_id'::uuid,
  recipe.id,
  ingredient.id,
  NULL,
  18,
  gram_unit.id,
  false
FROM recipe
CROSS JOIN ingredient
CROSS JOIN gram_unit
ON CONFLICT (id) DO UPDATE SET
  ingredient_product_id = EXCLUDED.ingredient_product_id,
  ingredient_variant_id = EXCLUDED.ingredient_variant_id,
  quantity = EXCLUDED.quantity,
  unit_id = EXCLUDED.unit_id,
  optional = EXCLUDED.optional;

COMMIT;

SELECT
  p.sku AS output_sku,
  r.id AS recipe_id,
  ip.sku AS ingredient_sku,
  ri.quantity,
  u.code AS unit_code
FROM pos.recipes r
JOIN pos.products p ON p.tenant_id = r.tenant_id AND p.id = r.output_product_id
JOIN pos.recipe_items ri ON ri.tenant_id = r.tenant_id AND ri.recipe_id = r.id
JOIN pos.products ip ON ip.tenant_id = ri.tenant_id AND ip.id = ri.ingredient_product_id
JOIN pos.units u ON u.tenant_id = ri.tenant_id AND u.id = ri.unit_id
WHERE r.tenant_id = :'tenant_id'::uuid
  AND p.sku = 'QSR-AMERICANO'
  AND r.status = 'active'
ORDER BY ri.created_at;
