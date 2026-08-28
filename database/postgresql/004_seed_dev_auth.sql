-- Optional development seed.
-- Do not run in production.

BEGIN;

SET search_path TO pos, extensions, public;

INSERT INTO tenants (id, name, legal_name, status)
VALUES (
  '11111111-1111-1111-1111-111111111111',
  'SolidPOS Demo Cafe',
  'SolidPOS Demo Cafe',
  'active'
)
ON CONFLICT (id) DO UPDATE SET
  name = EXCLUDED.name,
  legal_name = EXCLUDED.legal_name,
  status = EXCLUDED.status,
  updated_at = now();

SELECT set_config('app.tenant_id', '11111111-1111-1111-1111-111111111111', true);

INSERT INTO tenant_configs (tenant_id, business_vertical, ui_layout, modules_enabled, branding)
VALUES (
  '11111111-1111-1111-1111-111111111111',
  'qsr_cafe',
  'touch_grid',
  '{"modifiers": true, "recipes_bom": true, "barcode": true, "cash": true, "sync": true}'::jsonb,
  '{"name": "SolidPOS Demo Cafe", "primaryColor": "#111827"}'::jsonb
)
ON CONFLICT (tenant_id) DO UPDATE SET
  business_vertical = EXCLUDED.business_vertical,
  ui_layout = EXCLUDED.ui_layout,
  modules_enabled = EXCLUDED.modules_enabled,
  branding = EXCLUDED.branding,
  updated_at = now();

INSERT INTO stores (id, tenant_id, code, name, status)
VALUES (
  '22222222-2222-2222-2222-222222222222',
  '11111111-1111-1111-1111-111111111111',
  'MAIN',
  'Main Store',
  'active'
)
ON CONFLICT (tenant_id, code) DO UPDATE SET
  name = EXCLUDED.name,
  status = EXCLUDED.status,
  updated_at = now();

SELECT pos.seed_mvp_roles('11111111-1111-1111-1111-111111111111');

INSERT INTO users (id, tenant_id, email, password_hash, full_name, status)
VALUES (
  '33333333-3333-3333-3333-333333333333',
  '11111111-1111-1111-1111-111111111111',
  'owner@solidpos.local',
  crypt('Admin123!', gen_salt('bf', 12)),
  'SolidPOS Owner',
  'active'
)
ON CONFLICT (tenant_id, email) DO UPDATE SET
  password_hash = EXCLUDED.password_hash,
  full_name = EXCLUDED.full_name,
  status = EXCLUDED.status,
  updated_at = now();

INSERT INTO user_roles (tenant_id, user_id, role_id)
SELECT
  '11111111-1111-1111-1111-111111111111',
  '33333333-3333-3333-3333-333333333333',
  r.id
FROM roles r
WHERE r.tenant_id = '11111111-1111-1111-1111-111111111111'
  AND r.code = 'owner'
ON CONFLICT DO NOTHING;

INSERT INTO unit_families (id, tenant_id, code, name)
VALUES
  ('10000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', 'unit', 'Unit'),
  ('10000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', 'weight', 'Weight'),
  ('10000000-0000-0000-0000-000000000003', '11111111-1111-1111-1111-111111111111', 'volume', 'Volume')
ON CONFLICT (tenant_id, code) DO UPDATE SET name = EXCLUDED.name;

INSERT INTO units (id, tenant_id, family_id, code, name, symbol, factor_to_base, is_base)
VALUES
  ('11000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000001', 'unit', 'Unit', 'u', 1, true),
  ('11000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000002', 'g', 'Gram', 'g', 1, true),
  ('11000000-0000-0000-0000-000000000003', '11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000003', 'ml', 'Milliliter', 'ml', 1, true)
ON CONFLICT (tenant_id, code) DO UPDATE SET
  name = EXCLUDED.name,
  symbol = EXCLUDED.symbol,
  factor_to_base = EXCLUDED.factor_to_base,
  is_base = EXCLUDED.is_base;

INSERT INTO categories (id, tenant_id, parent_id, name, sort_order, status)
VALUES
  ('20000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', NULL, 'Bebidas', 10, 'active'),
  ('20000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', NULL, 'Alimentos', 20, 'active')
ON CONFLICT (id) DO UPDATE SET
  name = EXCLUDED.name,
  sort_order = EXCLUDED.sort_order,
  status = EXCLUDED.status,
  updated_at = now();

INSERT INTO products (
  id, tenant_id, category_id, sku, name, description, product_type,
  sale_unit_id, inventory_unit_id, is_sellable, is_stock_tracked,
  allow_negative_stock, tax_mode, attributes, status
)
VALUES
  ('30000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', '20000000-0000-0000-0000-000000000001', 'LATTE-12', 'Latte 12oz', 'Cafe latte caliente 12oz', 'recipe_item', '11000000-0000-0000-0000-000000000001', NULL, true, false, true, 'taxable', '{"touchColor": "#7c3aed", "prepArea": "bar"}'::jsonb, 'active'),
  ('30000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', '20000000-0000-0000-0000-000000000001', 'AMERICANO-12', 'Americano 12oz', 'Cafe americano 12oz', 'recipe_item', '11000000-0000-0000-0000-000000000001', NULL, true, false, true, 'taxable', '{"touchColor": "#2563eb", "prepArea": "bar"}'::jsonb, 'active'),
  ('30000000-0000-0000-0000-000000000003', '11111111-1111-1111-1111-111111111111', '20000000-0000-0000-0000-000000000002', 'PANINI-001', 'Panini', 'Panini demo', 'simple', '11000000-0000-0000-0000-000000000001', '11000000-0000-0000-0000-000000000001', true, true, true, 'taxable', '{"touchColor": "#ea580c", "prepArea": "kitchen"}'::jsonb, 'active'),
  ('30000000-0000-0000-0000-000000000008', '11111111-1111-1111-1111-111111111111', '20000000-0000-0000-0000-000000000001', 'FRAPPE', 'Frappe', 'Frappe base con variantes de tamano', 'variant_parent', '11000000-0000-0000-0000-000000000001', NULL, true, false, true, 'taxable', '{"touchColor": "#0891b2", "prepArea": "bar"}'::jsonb, 'active'),
  ('30000000-0000-0000-0000-000000000004', '11111111-1111-1111-1111-111111111111', NULL, 'ING-CAFE-G', 'Cafe molido', NULL, 'ingredient', NULL, '11000000-0000-0000-0000-000000000002', false, true, true, 'exempt', '{"ingredient": true}'::jsonb, 'active'),
  ('30000000-0000-0000-0000-000000000005', '11111111-1111-1111-1111-111111111111', NULL, 'ING-LECHE-ML', 'Leche entera', NULL, 'ingredient', NULL, '11000000-0000-0000-0000-000000000003', false, true, true, 'exempt', '{"ingredient": true}'::jsonb, 'active'),
  ('30000000-0000-0000-0000-000000000006', '11111111-1111-1111-1111-111111111111', NULL, 'ING-AVENA-ML', 'Leche de avena', NULL, 'ingredient', NULL, '11000000-0000-0000-0000-000000000003', false, true, true, 'exempt', '{"ingredient": true}'::jsonb, 'active'),
  ('30000000-0000-0000-0000-000000000007', '11111111-1111-1111-1111-111111111111', NULL, 'ING-VASO-12', 'Vaso 12oz', NULL, 'ingredient', NULL, '11000000-0000-0000-0000-000000000001', false, true, true, 'exempt', '{"ingredient": true}'::jsonb, 'active')
ON CONFLICT (tenant_id, sku) DO UPDATE SET
  category_id = EXCLUDED.category_id,
  name = EXCLUDED.name,
  description = EXCLUDED.description,
  product_type = EXCLUDED.product_type,
  sale_unit_id = EXCLUDED.sale_unit_id,
  inventory_unit_id = EXCLUDED.inventory_unit_id,
  is_sellable = EXCLUDED.is_sellable,
  is_stock_tracked = EXCLUDED.is_stock_tracked,
  allow_negative_stock = EXCLUDED.allow_negative_stock,
  tax_mode = EXCLUDED.tax_mode,
  attributes = EXCLUDED.attributes,
  status = EXCLUDED.status,
  updated_at = now();

INSERT INTO product_variants (id, tenant_id, product_id, sku, name, attributes, status)
VALUES
  ('31000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', '30000000-0000-0000-0000-000000000008', 'FRAPPE-12', 'Frappe 12oz', '{"size": "12oz"}'::jsonb, 'active'),
  ('31000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', '30000000-0000-0000-0000-000000000008', 'FRAPPE-16', 'Frappe 16oz', '{"size": "16oz"}'::jsonb, 'active')
ON CONFLICT (tenant_id, sku) DO UPDATE SET
  product_id = EXCLUDED.product_id,
  name = EXCLUDED.name,
  attributes = EXCLUDED.attributes,
  status = EXCLUDED.status,
  updated_at = now();

INSERT INTO product_barcodes (id, tenant_id, product_id, barcode, quantity, unit_id)
VALUES
  ('32000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', '30000000-0000-0000-0000-000000000003', '7500000000010', 1, '11000000-0000-0000-0000-000000000001')
ON CONFLICT (tenant_id, barcode) DO UPDATE SET
  product_id = EXCLUDED.product_id,
  quantity = EXCLUDED.quantity,
  unit_id = EXCLUDED.unit_id,
  deleted_at = NULL;

INSERT INTO price_lists (id, tenant_id, code, name, currency, status)
VALUES ('40000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', 'DEFAULT', 'Default', 'MXN', 'active')
ON CONFLICT (tenant_id, code) DO UPDATE SET
  name = EXCLUDED.name,
  currency = EXCLUDED.currency,
  status = EXCLUDED.status,
  updated_at = now();

INSERT INTO product_prices (id, tenant_id, price_list_id, product_id, variant_id, price_cents, currency)
VALUES
  ('41000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', '40000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001', NULL, 6500, 'MXN'),
  ('41000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', '40000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000002', NULL, 4500, 'MXN'),
  ('41000000-0000-0000-0000-000000000003', '11111111-1111-1111-1111-111111111111', '40000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000003', NULL, 8500, 'MXN'),
  ('41000000-0000-0000-0000-000000000004', '11111111-1111-1111-1111-111111111111', '40000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000008', '31000000-0000-0000-0000-000000000001', 7000, 'MXN'),
  ('41000000-0000-0000-0000-000000000005', '11111111-1111-1111-1111-111111111111', '40000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000008', '31000000-0000-0000-0000-000000000002', 8200, 'MXN')
ON CONFLICT (id) DO UPDATE SET
  price_cents = EXCLUDED.price_cents,
  currency = EXCLUDED.currency,
  deleted_at = NULL;

INSERT INTO payment_methods (id, tenant_id, code, name, method_type, status)
VALUES
  ('42000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', 'cash', 'Efectivo', 'cash', 'active'),
  ('42000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', 'card_manual', 'Tarjeta manual', 'card', 'active'),
  ('42000000-0000-0000-0000-000000000003', '11111111-1111-1111-1111-111111111111', 'transfer', 'Transferencia', 'transfer', 'active')
ON CONFLICT (tenant_id, code) DO UPDATE SET
  name = EXCLUDED.name,
  method_type = EXCLUDED.method_type,
  status = EXCLUDED.status,
  updated_at = now(),
  deleted_at = NULL;

INSERT INTO modifier_groups (id, tenant_id, name, min_selected, max_selected, required)
VALUES ('50000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', 'Tipo de leche', 1, 1, true)
ON CONFLICT (id) DO UPDATE SET
  name = EXCLUDED.name,
  min_selected = EXCLUDED.min_selected,
  max_selected = EXCLUDED.max_selected,
  required = EXCLUDED.required,
  updated_at = now();

INSERT INTO modifiers (
  id, tenant_id, group_id, name, price_delta_cents, linked_product_id, linked_variant_id,
  inventory_behavior, consumption_quantity, consumption_unit_id, replaces_product_id, replaces_variant_id
)
VALUES
  (
    '51000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111',
    '50000000-0000-0000-0000-000000000001', 'Leche entera', 0,
    '30000000-0000-0000-0000-000000000005', NULL,
    'none', NULL, NULL, NULL, NULL
  ),
  (
    '51000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111',
    '50000000-0000-0000-0000-000000000001', 'Leche de avena', 800,
    '30000000-0000-0000-0000-000000000006', NULL,
    'substitute', 250, '11000000-0000-0000-0000-000000000003',
    '30000000-0000-0000-0000-000000000005', NULL
  )
ON CONFLICT (id) DO UPDATE SET
  name = EXCLUDED.name,
  price_delta_cents = EXCLUDED.price_delta_cents,
  linked_product_id = EXCLUDED.linked_product_id,
  linked_variant_id = EXCLUDED.linked_variant_id,
  inventory_behavior = EXCLUDED.inventory_behavior,
  consumption_quantity = EXCLUDED.consumption_quantity,
  consumption_unit_id = EXCLUDED.consumption_unit_id,
  replaces_product_id = EXCLUDED.replaces_product_id,
  replaces_variant_id = EXCLUDED.replaces_variant_id,
  updated_at = now();

INSERT INTO product_modifier_groups (tenant_id, product_id, modifier_group_id)
VALUES ('11111111-1111-1111-1111-111111111111', '30000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000001')
ON CONFLICT DO NOTHING;

INSERT INTO recipes (id, tenant_id, output_product_id, version, yield_quantity, yield_unit_id, waste_percent, status)
VALUES
  ('60000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', '30000000-0000-0000-0000-000000000001', 1, 1, '11000000-0000-0000-0000-000000000001', 0, 'active'),
  ('60000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', '30000000-0000-0000-0000-000000000002', 1, 1, '11000000-0000-0000-0000-000000000001', 0, 'active')
ON CONFLICT (id) DO UPDATE SET
  yield_quantity = EXCLUDED.yield_quantity,
  yield_unit_id = EXCLUDED.yield_unit_id,
  waste_percent = EXCLUDED.waste_percent,
  status = EXCLUDED.status,
  updated_at = now();

INSERT INTO recipe_items (id, tenant_id, recipe_id, ingredient_product_id, quantity, unit_id, optional)
VALUES
  ('61000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', '60000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000004', 18, '11000000-0000-0000-0000-000000000002', false),
  ('61000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', '60000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000005', 250, '11000000-0000-0000-0000-000000000003', false),
  ('61000000-0000-0000-0000-000000000003', '11111111-1111-1111-1111-111111111111', '60000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000007', 1, '11000000-0000-0000-0000-000000000001', false),
  ('61000000-0000-0000-0000-000000000004', '11111111-1111-1111-1111-111111111111', '60000000-0000-0000-0000-000000000002', '30000000-0000-0000-0000-000000000004', 18, '11000000-0000-0000-0000-000000000002', false),
  ('61000000-0000-0000-0000-000000000005', '11111111-1111-1111-1111-111111111111', '60000000-0000-0000-0000-000000000002', '30000000-0000-0000-0000-000000000007', 1, '11000000-0000-0000-0000-000000000001', false)
ON CONFLICT (id) DO UPDATE SET
  quantity = EXCLUDED.quantity,
  unit_id = EXCLUDED.unit_id,
  optional = EXCLUDED.optional;

COMMIT;
