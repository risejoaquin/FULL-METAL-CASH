\set ON_ERROR_STOP on

BEGIN;
SET search_path TO pos, public;
SELECT set_config('app.tenant_id', :'tenant_id', true);

WITH unit_family AS (
  INSERT INTO pos.unit_families (tenant_id, code, name)
  VALUES (:'tenant_id'::uuid, 'quantity', 'Cantidad')
  ON CONFLICT (tenant_id, code) DO UPDATE SET name = EXCLUDED.name
  RETURNING id
), unit_unit AS (
  INSERT INTO pos.units (tenant_id, family_id, code, name, symbol, factor_to_base, is_base)
  SELECT :'tenant_id'::uuid, id, 'unit', 'Unidad', 'u', 1, true
  FROM unit_family
  ON CONFLICT (tenant_id, code) DO UPDATE SET
    name = EXCLUDED.name,
    symbol = EXCLUDED.symbol,
    factor_to_base = EXCLUDED.factor_to_base,
    is_base = EXCLUDED.is_base
  RETURNING id
), category AS (
  INSERT INTO pos.categories (tenant_id, name, sort_order, status)
  SELECT :'tenant_id'::uuid, 'Bebidas', 10, 'active'
  WHERE NOT EXISTS (
    SELECT 1 FROM pos.categories
    WHERE tenant_id = :'tenant_id'::uuid
      AND name = 'Bebidas'
      AND deleted_at IS NULL
  )
  RETURNING id
), selected_category AS (
  SELECT id FROM category
  UNION ALL
  SELECT id FROM pos.categories
  WHERE tenant_id = :'tenant_id'::uuid
    AND name = 'Bebidas'
    AND deleted_at IS NULL
  LIMIT 1
), product AS (
  INSERT INTO pos.products (
    tenant_id,
    category_id,
    sku,
    name,
    description,
    product_type,
    sale_unit_id,
    inventory_unit_id,
    is_sellable,
    is_stock_tracked,
    allow_negative_stock,
    tax_mode,
    status
  )
  SELECT
    :'tenant_id'::uuid,
    selected_category.id,
    'QSR-AMERICANO',
    'Americano 12oz',
    'Producto operativo inicial para pruebas productivas de POS.',
    'simple',
    unit_unit.id,
    unit_unit.id,
    true,
    false,
    true,
    'exempt',
    'active'
  FROM selected_category
  CROSS JOIN unit_unit
  ON CONFLICT (tenant_id, sku) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    category_id = EXCLUDED.category_id,
    sale_unit_id = EXCLUDED.sale_unit_id,
    inventory_unit_id = EXCLUDED.inventory_unit_id,
    is_sellable = true,
    is_stock_tracked = false,
    allow_negative_stock = true,
    tax_mode = 'exempt',
    status = 'active',
    deleted_at = NULL,
    updated_at = now()
  RETURNING id
), price_list AS (
  INSERT INTO pos.price_lists (tenant_id, code, name, currency, status)
  VALUES (:'tenant_id'::uuid, 'default', 'Lista general', :'currency', 'active')
  ON CONFLICT (tenant_id, code) DO UPDATE SET
    name = EXCLUDED.name,
    currency = EXCLUDED.currency,
    status = 'active',
    deleted_at = NULL,
    updated_at = now()
  RETURNING id
)
INSERT INTO pos.product_prices (tenant_id, price_list_id, product_id, variant_id, price_cents, currency)
SELECT :'tenant_id'::uuid, price_list.id, product.id, NULL, 4500, :'currency'
FROM price_list
CROSS JOIN product
WHERE NOT EXISTS (
  SELECT 1 FROM pos.product_prices pp
  WHERE pp.tenant_id = :'tenant_id'::uuid
    AND pp.price_list_id = price_list.id
    AND pp.product_id = product.id
    AND pp.variant_id IS NULL
    AND pp.deleted_at IS NULL
);

INSERT INTO pos.payment_methods (tenant_id, code, name, method_type, status)
VALUES
  (:'tenant_id'::uuid, 'cash', 'Efectivo', 'cash', 'active'),
  (:'tenant_id'::uuid, 'card_manual', 'Tarjeta manual', 'card', 'active'),
  (:'tenant_id'::uuid, 'transfer', 'Transferencia', 'transfer', 'active')
ON CONFLICT (tenant_id, code) DO UPDATE SET
  name = EXCLUDED.name,
  method_type = EXCLUDED.method_type,
  status = 'active',
  deleted_at = NULL,
  updated_at = now();

COMMIT;

SELECT
  p.id AS product_id,
  p.sku,
  p.name,
  pp.price_cents,
  pm_cash.id AS cash_payment_method_id
FROM pos.products p
JOIN pos.product_prices pp ON pp.tenant_id = p.tenant_id AND pp.product_id = p.id AND pp.deleted_at IS NULL
JOIN pos.payment_methods pm_cash ON pm_cash.tenant_id = p.tenant_id AND pm_cash.code = 'cash'
WHERE p.tenant_id = :'tenant_id'::uuid
  AND p.sku = 'QSR-AMERICANO';
