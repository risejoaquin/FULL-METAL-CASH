-- POS SaaS Offline-First - PostgreSQL Initial Schema
-- Target: PostgreSQL 15+
-- Principles: multi-tenant RLS, append-only inventory ledger, idempotency, ACID, soft delete, sync events.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS citext;

CREATE SCHEMA IF NOT EXISTS pos;

SET search_path TO pos, public;

CREATE OR REPLACE FUNCTION pos.current_tenant_id()
RETURNS uuid
LANGUAGE sql
STABLE
AS $$
  SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid
$$;

CREATE OR REPLACE FUNCTION pos.touch_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  NEW.updated_at = now();
  RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION pos.prevent_inventory_ledger_update_delete()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  RAISE EXCEPTION 'inventory_ledger is append-only';
END;
$$;

CREATE TABLE tenants (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name text NOT NULL,
  legal_name text,
  status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'suspended', 'closed')),
  timezone text NOT NULL DEFAULT 'America/Hermosillo',
  currency char(3) NOT NULL DEFAULT 'MXN',
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE TRIGGER trg_tenants_updated_at
BEFORE UPDATE ON tenants
FOR EACH ROW EXECUTE FUNCTION pos.touch_updated_at();

CREATE TABLE tenant_configs (
  tenant_id uuid PRIMARY KEY REFERENCES tenants(id),
  business_vertical text NOT NULL DEFAULT 'generic',
  ui_layout text NOT NULL DEFAULT 'standard',
  modules_enabled jsonb NOT NULL DEFAULT '{}'::jsonb,
  branding jsonb NOT NULL DEFAULT '{}'::jsonb,
  receipt_settings jsonb NOT NULL DEFAULT '{}'::jsonb,
  hardware_profile jsonb NOT NULL DEFAULT '{}'::jsonb,
  feature_flags jsonb NOT NULL DEFAULT '{}'::jsonb,
  version bigint NOT NULL DEFAULT 1,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TRIGGER trg_tenant_configs_updated_at
BEFORE UPDATE ON tenant_configs
FOR EACH ROW EXECUTE FUNCTION pos.touch_updated_at();

CREATE TABLE stores (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  code text NOT NULL,
  name text NOT NULL,
  address text,
  phone text,
  status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive', 'archived')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz,
  UNIQUE (tenant_id, code)
);

CREATE INDEX idx_stores_tenant_status ON stores (tenant_id, status) WHERE deleted_at IS NULL;
CREATE TRIGGER trg_stores_updated_at BEFORE UPDATE ON stores FOR EACH ROW EXECUTE FUNCTION pos.touch_updated_at();

CREATE TABLE users (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  email citext NOT NULL,
  password_hash text NOT NULL,
  full_name text NOT NULL,
  status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'suspended', 'invited')),
  pin_hash text,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz,
  UNIQUE (tenant_id, email)
);

CREATE INDEX idx_users_tenant_status ON users (tenant_id, status) WHERE deleted_at IS NULL;
CREATE TRIGGER trg_users_updated_at BEFORE UPDATE ON users FOR EACH ROW EXECUTE FUNCTION pos.touch_updated_at();

CREATE TABLE roles (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  code text NOT NULL,
  name text NOT NULL,
  is_system boolean NOT NULL DEFAULT false,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz,
  UNIQUE (tenant_id, code)
);

CREATE TABLE permissions (
  code text PRIMARY KEY,
  description text NOT NULL
);

CREATE TABLE role_permissions (
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  role_id uuid NOT NULL REFERENCES roles(id),
  permission_code text NOT NULL REFERENCES permissions(code),
  PRIMARY KEY (tenant_id, role_id, permission_code)
);

CREATE TABLE user_roles (
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  user_id uuid NOT NULL REFERENCES users(id),
  role_id uuid NOT NULL REFERENCES roles(id),
  PRIMARY KEY (tenant_id, user_id, role_id)
);

CREATE TABLE user_store_access (
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  user_id uuid NOT NULL REFERENCES users(id),
  store_id uuid NOT NULL REFERENCES stores(id),
  PRIMARY KEY (tenant_id, user_id, store_id)
);

CREATE TABLE refresh_tokens (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  user_id uuid NOT NULL REFERENCES users(id),
  token_hash text NOT NULL,
  replaced_by_token_id uuid REFERENCES refresh_tokens(id),
  expires_at timestamptz NOT NULL,
  revoked_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, token_hash)
);

CREATE TABLE terminals (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  store_id uuid NOT NULL REFERENCES stores(id),
  name text NOT NULL,
  fingerprint text NOT NULL,
  status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'active', 'blocked', 'retired')),
  device_token_hash text,
  app_version text,
  local_db_version integer NOT NULL DEFAULT 0,
  last_seen_at timestamptz,
  last_sync_cursor text,
  offline_grace_expires_at timestamptz,
  hard_locked_at timestamptz,
  hard_lock_reason text,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz,
  UNIQUE (tenant_id, fingerprint)
);

CREATE INDEX idx_terminals_tenant_store ON terminals (tenant_id, store_id, status);
CREATE TRIGGER trg_terminals_updated_at BEFORE UPDATE ON terminals FOR EACH ROW EXECUTE FUNCTION pos.touch_updated_at();

CREATE TABLE enrollment_tokens (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  store_id uuid NOT NULL REFERENCES stores(id),
  token_hash text NOT NULL,
  purpose text NOT NULL CHECK (purpose IN ('terminal_register', 'builder_package')),
  expires_at timestamptz NOT NULL,
  used_at timestamptz,
  revoked_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, token_hash)
);

CREATE TABLE terminal_offline_unlock_codes (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  terminal_id uuid NOT NULL REFERENCES terminals(id),
  code_hash text NOT NULL,
  reason text NOT NULL,
  valid_from timestamptz NOT NULL DEFAULT now(),
  expires_at timestamptz NOT NULL,
  used_at timestamptz,
  issued_by_user_id uuid REFERENCES users(id),
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, terminal_id, code_hash)
);

CREATE INDEX idx_terminal_unlock_codes_active
ON terminal_offline_unlock_codes (tenant_id, terminal_id, expires_at)
WHERE used_at IS NULL;

CREATE TABLE categories (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  parent_id uuid REFERENCES categories(id),
  name text NOT NULL,
  sort_order integer NOT NULL DEFAULT 0,
  status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive', 'archived')),
  version bigint NOT NULL DEFAULT 1,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE INDEX idx_categories_tenant_parent ON categories (tenant_id, parent_id) WHERE deleted_at IS NULL;
CREATE TRIGGER trg_categories_updated_at BEFORE UPDATE ON categories FOR EACH ROW EXECUTE FUNCTION pos.touch_updated_at();

CREATE TABLE unit_families (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  code text NOT NULL,
  name text NOT NULL,
  UNIQUE (tenant_id, code)
);

CREATE TABLE units (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  family_id uuid NOT NULL REFERENCES unit_families(id),
  code text NOT NULL,
  name text NOT NULL,
  symbol text NOT NULL,
  factor_to_base numeric(18,8) NOT NULL CHECK (factor_to_base > 0),
  is_base boolean NOT NULL DEFAULT false,
  UNIQUE (tenant_id, code)
);

CREATE TABLE products (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  category_id uuid REFERENCES categories(id),
  sku text NOT NULL,
  name text NOT NULL,
  description text,
  product_type text NOT NULL CHECK (product_type IN ('simple', 'variant_parent', 'ingredient', 'service', 'kit', 'combo', 'recipe_item')),
  sale_unit_id uuid REFERENCES units(id),
  inventory_unit_id uuid REFERENCES units(id),
  purchase_unit_id uuid REFERENCES units(id),
  is_sellable boolean NOT NULL DEFAULT true,
  is_stock_tracked boolean NOT NULL DEFAULT true,
  allow_negative_stock boolean NOT NULL DEFAULT false,
  tax_mode text NOT NULL DEFAULT 'taxable' CHECK (tax_mode IN ('taxable', 'exempt')),
  attributes jsonb NOT NULL DEFAULT '{}'::jsonb,
  status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive', 'archived')),
  version bigint NOT NULL DEFAULT 1,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz,
  UNIQUE (tenant_id, sku)
);

CREATE INDEX idx_products_tenant_name ON products USING gin (to_tsvector('simple', coalesce(name, '') || ' ' || coalesce(sku, '')));
CREATE INDEX idx_products_tenant_category ON products (tenant_id, category_id, status) WHERE deleted_at IS NULL;
CREATE INDEX idx_products_attributes_gin ON products USING gin (attributes);
CREATE TRIGGER trg_products_updated_at BEFORE UPDATE ON products FOR EACH ROW EXECUTE FUNCTION pos.touch_updated_at();

CREATE TABLE product_variants (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  product_id uuid NOT NULL REFERENCES products(id),
  sku text NOT NULL,
  name text NOT NULL,
  attributes jsonb NOT NULL DEFAULT '{}'::jsonb,
  status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive', 'archived')),
  version bigint NOT NULL DEFAULT 1,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz,
  UNIQUE (tenant_id, sku)
);

CREATE INDEX idx_product_variants_tenant_product ON product_variants (tenant_id, product_id) WHERE deleted_at IS NULL;

CREATE TABLE product_barcodes (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  product_id uuid NOT NULL REFERENCES products(id),
  variant_id uuid REFERENCES product_variants(id),
  barcode text NOT NULL,
  quantity numeric(18,4) NOT NULL DEFAULT 1,
  unit_id uuid REFERENCES units(id),
  created_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz,
  UNIQUE (tenant_id, barcode)
);

CREATE INDEX idx_product_barcodes_lookup ON product_barcodes (tenant_id, barcode) WHERE deleted_at IS NULL;

CREATE TABLE price_lists (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  code text NOT NULL,
  name text NOT NULL,
  currency char(3) NOT NULL DEFAULT 'MXN',
  status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive', 'archived')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz,
  UNIQUE (tenant_id, code)
);

CREATE TABLE product_prices (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  price_list_id uuid NOT NULL REFERENCES price_lists(id),
  product_id uuid NOT NULL REFERENCES products(id),
  variant_id uuid REFERENCES product_variants(id),
  price_cents bigint NOT NULL CHECK (price_cents >= 0),
  currency char(3) NOT NULL DEFAULT 'MXN',
  starts_at timestamptz,
  ends_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE INDEX idx_product_prices_lookup ON product_prices (tenant_id, price_list_id, product_id, variant_id, starts_at, ends_at) WHERE deleted_at IS NULL;

CREATE TABLE modifier_groups (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  name text NOT NULL,
  min_selected integer NOT NULL DEFAULT 0,
  max_selected integer NOT NULL DEFAULT 1,
  required boolean NOT NULL DEFAULT false,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE TABLE modifiers (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  group_id uuid NOT NULL REFERENCES modifier_groups(id),
  name text NOT NULL,
  price_delta_cents bigint NOT NULL DEFAULT 0,
  linked_product_id uuid REFERENCES products(id),
  linked_variant_id uuid REFERENCES product_variants(id),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE TABLE product_modifier_groups (
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  product_id uuid NOT NULL REFERENCES products(id),
  modifier_group_id uuid NOT NULL REFERENCES modifier_groups(id),
  PRIMARY KEY (tenant_id, product_id, modifier_group_id)
);

CREATE TABLE recipes (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  output_product_id uuid NOT NULL REFERENCES products(id),
  output_variant_id uuid REFERENCES product_variants(id),
  version integer NOT NULL DEFAULT 1,
  yield_quantity numeric(18,4) NOT NULL DEFAULT 1,
  yield_unit_id uuid NOT NULL REFERENCES units(id),
  waste_percent numeric(7,4) NOT NULL DEFAULT 0,
  status text NOT NULL DEFAULT 'active' CHECK (status IN ('draft', 'active', 'inactive', 'archived')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz,
  UNIQUE (tenant_id, output_product_id, output_variant_id, version)
);

CREATE TABLE recipe_items (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  recipe_id uuid NOT NULL REFERENCES recipes(id),
  ingredient_product_id uuid NOT NULL REFERENCES products(id),
  ingredient_variant_id uuid REFERENCES product_variants(id),
  quantity numeric(18,4) NOT NULL CHECK (quantity > 0),
  unit_id uuid NOT NULL REFERENCES units(id),
  optional boolean NOT NULL DEFAULT false,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_recipe_items_recipe ON recipe_items (tenant_id, recipe_id);

CREATE TABLE customers (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  name text NOT NULL,
  email citext,
  phone text,
  credit_limit_cents bigint NOT NULL DEFAULT 0,
  status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive', 'archived')),
  attributes jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE INDEX idx_customers_search ON customers USING gin (to_tsvector('simple', coalesce(name, '') || ' ' || coalesce(phone, '') || ' ' || coalesce(email::text, '')));

CREATE TABLE cash_shifts (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  store_id uuid NOT NULL REFERENCES stores(id),
  terminal_id uuid NOT NULL REFERENCES terminals(id),
  opened_by_user_id uuid NOT NULL REFERENCES users(id),
  closed_by_user_id uuid REFERENCES users(id),
  status text NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'closed', 'cancelled')),
  opening_amount_cents bigint NOT NULL DEFAULT 0,
  expected_cash_cents bigint NOT NULL DEFAULT 0,
  counted_cash_cents bigint,
  difference_cents bigint,
  opened_at timestamptz NOT NULL DEFAULT now(),
  closed_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  CHECK (
    (status = 'open' AND closed_at IS NULL)
    OR (status IN ('closed', 'cancelled'))
  )
);

CREATE INDEX idx_cash_shifts_tenant_store ON cash_shifts (tenant_id, store_id, opened_at DESC);
CREATE UNIQUE INDEX uq_cash_shifts_one_open_per_terminal
ON cash_shifts (tenant_id, terminal_id)
WHERE status = 'open';

CREATE TABLE cash_movements (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  cash_shift_id uuid NOT NULL REFERENCES cash_shifts(id),
  movement_type text NOT NULL CHECK (movement_type IN ('cash_in', 'cash_out', 'drawer_open_no_sale')),
  amount_cents bigint NOT NULL DEFAULT 0,
  reason text NOT NULL,
  authorized_by_user_id uuid REFERENCES users(id),
  created_by_user_id uuid NOT NULL REFERENCES users(id),
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_cash_movements_shift ON cash_movements (tenant_id, cash_shift_id, created_at);

CREATE TABLE sales (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  store_id uuid NOT NULL REFERENCES stores(id),
  terminal_id uuid NOT NULL REFERENCES terminals(id),
  cash_shift_id uuid REFERENCES cash_shifts(id),
  customer_id uuid REFERENCES customers(id),
  cashier_user_id uuid NOT NULL REFERENCES users(id),
  local_sale_id uuid NOT NULL,
  status text NOT NULL DEFAULT 'completed' CHECK (status IN ('suspended', 'completed', 'voided', 'partially_returned', 'returned')),
  subtotal_cents bigint NOT NULL DEFAULT 0,
  discount_cents bigint NOT NULL DEFAULT 0,
  tax_cents bigint NOT NULL DEFAULT 0,
  tip_cents bigint NOT NULL DEFAULT 0,
  total_cents bigint NOT NULL DEFAULT 0,
  paid_cents bigint NOT NULL DEFAULT 0,
  change_cents bigint NOT NULL DEFAULT 0,
  currency char(3) NOT NULL DEFAULT 'MXN',
  occurred_at timestamptz NOT NULL,
  local_created_at timestamptz NOT NULL,
  version bigint NOT NULL DEFAULT 1,
  metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz,
  CHECK (status = 'suspended' OR cash_shift_id IS NOT NULL),
  UNIQUE (tenant_id, terminal_id, local_sale_id)
);

CREATE INDEX idx_sales_tenant_store_date ON sales (tenant_id, store_id, occurred_at DESC);
CREATE INDEX idx_sales_tenant_customer ON sales (tenant_id, customer_id, occurred_at DESC) WHERE customer_id IS NOT NULL;

CREATE TABLE sale_lines (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  sale_id uuid NOT NULL REFERENCES sales(id),
  product_id uuid NOT NULL REFERENCES products(id),
  variant_id uuid REFERENCES product_variants(id),
  line_number integer NOT NULL,
  description text NOT NULL,
  quantity numeric(18,4) NOT NULL CHECK (quantity > 0),
  unit_id uuid REFERENCES units(id),
  unit_price_cents bigint NOT NULL CHECK (unit_price_cents >= 0),
  discount_cents bigint NOT NULL DEFAULT 0,
  tax_cents bigint NOT NULL DEFAULT 0,
  total_cents bigint NOT NULL,
  recipe_id uuid REFERENCES recipes(id),
  preparation_note text,
  modifiers jsonb NOT NULL DEFAULT '[]'::jsonb,
  snapshot jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, sale_id, line_number)
);

CREATE INDEX idx_sale_lines_sale ON sale_lines (tenant_id, sale_id);

CREATE TABLE payment_methods (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  code text NOT NULL,
  name text NOT NULL,
  method_type text NOT NULL CHECK (method_type IN ('cash', 'card', 'transfer', 'wallet', 'customer_credit', 'gift_card', 'other')),
  status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive', 'archived')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz,
  UNIQUE (tenant_id, code)
);

CREATE TABLE payments (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  sale_id uuid NOT NULL REFERENCES sales(id),
  payment_method_id uuid NOT NULL REFERENCES payment_methods(id),
  local_payment_id uuid NOT NULL,
  amount_cents bigint NOT NULL CHECK (amount_cents >= 0),
  currency char(3) NOT NULL DEFAULT 'MXN',
  status text NOT NULL DEFAULT 'approved' CHECK (status IN ('approved', 'declined', 'voided', 'refunded')),
  reference text,
  metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, sale_id, local_payment_id)
);

CREATE INDEX idx_payments_sale ON payments (tenant_id, sale_id);

CREATE TABLE returns (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  sale_id uuid NOT NULL REFERENCES sales(id),
  cash_shift_id uuid REFERENCES cash_shifts(id),
  local_return_id uuid NOT NULL,
  status text NOT NULL DEFAULT 'completed' CHECK (status IN ('completed', 'cancelled')),
  reason text NOT NULL,
  subtotal_cents bigint NOT NULL DEFAULT 0,
  tax_cents bigint NOT NULL DEFAULT 0,
  total_cents bigint NOT NULL DEFAULT 0,
  refund_cents bigint NOT NULL DEFAULT 0,
  created_by_user_id uuid NOT NULL REFERENCES users(id),
  occurred_at timestamptz NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
  UNIQUE (tenant_id, sale_id, local_return_id)
);

CREATE TABLE return_lines (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  return_id uuid NOT NULL REFERENCES returns(id),
  sale_line_id uuid NOT NULL REFERENCES sale_lines(id),
  quantity numeric(18,4) NOT NULL CHECK (quantity > 0),
  total_cents bigint NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_returns_tenant_sale ON returns (tenant_id, sale_id, occurred_at DESC);
CREATE INDEX idx_returns_tenant_occurred ON returns (tenant_id, occurred_at DESC);
CREATE INDEX idx_return_lines_sale_line ON return_lines (tenant_id, sale_line_id);

CREATE TABLE return_refunds (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  return_id uuid NOT NULL REFERENCES returns(id),
  payment_method_id uuid NOT NULL REFERENCES payment_methods(id),
  method_code text NOT NULL,
  method_type text NOT NULL CHECK (method_type IN ('cash', 'card', 'transfer', 'wallet', 'customer_credit', 'gift_card', 'other')),
  amount_cents bigint NOT NULL CHECK (amount_cents > 0),
  currency char(3) NOT NULL DEFAULT 'MXN',
  status text NOT NULL DEFAULT 'approved' CHECK (status IN ('approved', 'declined', 'voided')),
  reference text,
  metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_return_refunds_return ON return_refunds (tenant_id, return_id, created_at);

CREATE TABLE digital_receipts (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  sale_id uuid NOT NULL REFERENCES sales(id),
  receipt_number text NOT NULL,
  public_token_hash text NOT NULL,
  public_url text NOT NULL,
  status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'revoked', 'expired')),
  expires_at timestamptz,
  issued_at timestamptz NOT NULL DEFAULT now(),
  created_at timestamptz NOT NULL DEFAULT now(),
  revoked_at timestamptz,
  last_sent_at timestamptz,
  last_sent_email text,
  send_count integer NOT NULL DEFAULT 0,
  metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
  UNIQUE (tenant_id, sale_id),
  UNIQUE (tenant_id, public_token_hash),
  UNIQUE (tenant_id, receipt_number)
);

CREATE INDEX idx_digital_receipts_public_lookup
ON digital_receipts (tenant_id, public_token_hash)
WHERE status = 'active';

CREATE INDEX idx_digital_receipts_sale_status
ON digital_receipts (tenant_id, sale_id, status);

CREATE TABLE inventory_ledger (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  store_id uuid NOT NULL REFERENCES stores(id),
  terminal_id uuid REFERENCES terminals(id),
  product_id uuid NOT NULL REFERENCES products(id),
  variant_id uuid REFERENCES product_variants(id),
  movement_type text NOT NULL CHECK (movement_type IN (
    'purchase_receipt',
    'sale',
    'sale_recipe_component',
    'return',
    'waste',
    'adjustment',
    'transfer_out',
    'transfer_in',
    'production',
    'stock_count',
    'void_compensation'
  )),
  quantity_delta numeric(18,4) NOT NULL,
  unit_id uuid NOT NULL REFERENCES units(id),
  cost_cents bigint,
  reference_type text,
  reference_id uuid,
  source_event_id uuid,
  local_occurred_at timestamptz NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  metadata jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE INDEX idx_inventory_ledger_stock ON inventory_ledger (tenant_id, store_id, product_id, variant_id, created_at);
CREATE INDEX idx_inventory_ledger_reference ON inventory_ledger (tenant_id, reference_type, reference_id);

CREATE TRIGGER trg_inventory_ledger_no_update
BEFORE UPDATE ON inventory_ledger
FOR EACH ROW EXECUTE FUNCTION pos.prevent_inventory_ledger_update_delete();

CREATE TRIGGER trg_inventory_ledger_no_delete
BEFORE DELETE ON inventory_ledger
FOR EACH ROW EXECUTE FUNCTION pos.prevent_inventory_ledger_update_delete();

CREATE VIEW inventory_stock AS
SELECT
  tenant_id,
  store_id,
  product_id,
  variant_id,
  unit_id,
  sum(quantity_delta) AS quantity_on_hand
FROM inventory_ledger
GROUP BY tenant_id, store_id, product_id, variant_id, unit_id;

CREATE TABLE purchase_orders (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  store_id uuid NOT NULL REFERENCES stores(id),
  supplier_name text NOT NULL,
  status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'ordered', 'received', 'cancelled')),
  total_cents bigint NOT NULL DEFAULT 0,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE TABLE purchase_order_lines (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  purchase_order_id uuid NOT NULL REFERENCES purchase_orders(id),
  product_id uuid NOT NULL REFERENCES products(id),
  variant_id uuid REFERENCES product_variants(id),
  quantity numeric(18,4) NOT NULL CHECK (quantity > 0),
  unit_id uuid NOT NULL REFERENCES units(id),
  unit_cost_cents bigint NOT NULL DEFAULT 0
);

CREATE TABLE stock_counts (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  store_id uuid NOT NULL REFERENCES stores(id),
  status text NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'applied', 'cancelled')),
  created_by_user_id uuid NOT NULL REFERENCES users(id),
  applied_by_user_id uuid REFERENCES users(id),
  created_at timestamptz NOT NULL DEFAULT now(),
  applied_at timestamptz
);

CREATE TABLE stock_count_lines (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  stock_count_id uuid NOT NULL REFERENCES stock_counts(id),
  product_id uuid NOT NULL REFERENCES products(id),
  variant_id uuid REFERENCES product_variants(id),
  counted_quantity numeric(18,4) NOT NULL CHECK (counted_quantity >= 0),
  unit_id uuid NOT NULL REFERENCES units(id)
);

CREATE TABLE discounts (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  code text,
  name text NOT NULL,
  discount_type text NOT NULL CHECK (discount_type IN ('percentage', 'fixed_amount', 'buy_x_get_y', 'combo_price')),
  value numeric(18,4) NOT NULL DEFAULT 0,
  rules jsonb NOT NULL DEFAULT '{}'::jsonb,
  starts_at timestamptz,
  ends_at timestamptz,
  status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive', 'archived')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz,
  UNIQUE (tenant_id, code)
);

CREATE TABLE loyalty_accounts (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  customer_id uuid NOT NULL REFERENCES customers(id),
  points_balance numeric(18,4) NOT NULL DEFAULT 0,
  wallet_balance_cents bigint NOT NULL DEFAULT 0,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, customer_id)
);

CREATE TABLE production_orders (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  store_id uuid NOT NULL REFERENCES stores(id),
  sale_id uuid REFERENCES sales(id),
  status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'preparing', 'ready', 'delivered', 'cancelled')),
  station text NOT NULL,
  priority integer NOT NULL DEFAULT 0,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE production_order_lines (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  production_order_id uuid NOT NULL REFERENCES production_orders(id),
  sale_line_id uuid REFERENCES sale_lines(id),
  product_name text NOT NULL,
  quantity numeric(18,4) NOT NULL,
  modifiers jsonb NOT NULL DEFAULT '[]'::jsonb,
  note text,
  status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'preparing', 'ready', 'delivered', 'cancelled'))
);

CREATE TABLE idempotency_keys (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  scope text NOT NULL,
  idempotency_key text NOT NULL,
  request_hash text NOT NULL,
  response_status integer,
  response_body jsonb,
  status text NOT NULL DEFAULT 'processing' CHECK (status IN ('processing', 'completed', 'failed')),
  locked_until timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, scope, idempotency_key)
);

CREATE INDEX idx_idempotency_processing ON idempotency_keys (tenant_id, status, locked_until);

CREATE TABLE sync_inbox_events (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  terminal_id uuid NOT NULL REFERENCES terminals(id),
  store_id uuid NOT NULL REFERENCES stores(id),
  event_id uuid NOT NULL,
  event_type text NOT NULL,
  entity_type text NOT NULL,
  entity_id uuid,
  local_occurred_at timestamptz NOT NULL,
  payload_hash text NOT NULL,
  payload jsonb NOT NULL,
  status text NOT NULL DEFAULT 'received' CHECK (status IN ('received', 'processed', 'duplicate', 'rejected', 'conflict')),
  result jsonb,
  error_code text,
  error_message text,
  created_at timestamptz NOT NULL DEFAULT now(),
  processed_at timestamptz,
  UNIQUE (tenant_id, terminal_id, event_id)
);

CREATE INDEX idx_sync_inbox_tenant_status ON sync_inbox_events (tenant_id, status, created_at);
CREATE INDEX idx_sync_inbox_entity ON sync_inbox_events (tenant_id, entity_type, entity_id);

CREATE TABLE sync_changes (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  store_id uuid REFERENCES stores(id),
  entity_type text NOT NULL,
  entity_id uuid NOT NULL,
  operation text NOT NULL CHECK (operation IN ('create', 'update', 'delete')),
  entity_version bigint NOT NULL,
  changed_at timestamptz NOT NULL DEFAULT now(),
  payload jsonb NOT NULL,
  source_terminal_id uuid REFERENCES terminals(id)
);

CREATE INDEX idx_sync_changes_cursor ON sync_changes (tenant_id, changed_at, id);
CREATE INDEX idx_sync_changes_store_cursor ON sync_changes (tenant_id, store_id, changed_at, id);

CREATE TABLE sync_conflicts (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  terminal_id uuid REFERENCES terminals(id),
  entity_type text NOT NULL,
  entity_id uuid NOT NULL,
  local_event_id uuid,
  local_version bigint,
  server_version bigint,
  local_payload jsonb NOT NULL,
  server_payload jsonb NOT NULL,
  resolution_strategy text CHECK (resolution_strategy IN ('use_server', 'use_client', 'merge', 'compensate')),
  resolved_payload jsonb,
  status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'resolved', 'ignored')),
  created_at timestamptz NOT NULL DEFAULT now(),
  resolved_at timestamptz
);

CREATE TABLE audit_events (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  actor_user_id uuid REFERENCES users(id),
  terminal_id uuid REFERENCES terminals(id),
  action text NOT NULL,
  entity_type text NOT NULL,
  entity_id uuid,
  before_data jsonb,
  after_data jsonb,
  ip_address inet,
  user_agent text,
  trace_id text,
  occurred_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_audit_events_tenant_time ON audit_events (tenant_id, occurred_at DESC);
CREATE INDEX idx_audit_events_entity ON audit_events (tenant_id, entity_type, entity_id);

CREATE TABLE builder_projects (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  name text NOT NULL,
  target_store_id uuid REFERENCES stores(id),
  package_type text NOT NULL DEFAULT 'velopack' CHECK (package_type IN ('velopack')),
  config jsonb NOT NULL DEFAULT '{}'::jsonb,
  branding jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE TABLE builder_builds (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  project_id uuid NOT NULL REFERENCES builder_projects(id),
  status text NOT NULL DEFAULT 'queued' CHECK (status IN ('queued', 'running', 'succeeded', 'failed')),
  app_version text NOT NULL,
  channel text NOT NULL CHECK (channel IN ('stable', 'beta', 'internal')),
  universal_installer boolean NOT NULL DEFAULT true,
  artifact_url text,
  artifact_hash text,
  signature text,
  error_message text,
  created_at timestamptz NOT NULL DEFAULT now(),
  finished_at timestamptz
);

CREATE TABLE update_releases (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid REFERENCES tenants(id),
  version text NOT NULL,
  channel text NOT NULL CHECK (channel IN ('stable', 'beta', 'internal')),
  package_type text NOT NULL DEFAULT 'velopack' CHECK (package_type IN ('velopack')),
  artifact_url text NOT NULL,
  artifact_hash text NOT NULL,
  signature text NOT NULL,
  rollback_version text,
  mandatory boolean NOT NULL DEFAULT false,
  universal_installer boolean NOT NULL DEFAULT true,
  published_at timestamptz NOT NULL DEFAULT now(),
  revoked_at timestamptz,
  UNIQUE (tenant_id, channel, package_type, version)
);

CREATE TABLE webhooks (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  name text NOT NULL,
  url text NOT NULL,
  secret_hash text NOT NULL,
  event_types text[] NOT NULL,
  status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive', 'archived')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE TABLE webhook_deliveries (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  webhook_id uuid NOT NULL REFERENCES webhooks(id),
  event_type text NOT NULL,
  payload jsonb NOT NULL,
  status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'delivered', 'failed')),
  attempts integer NOT NULL DEFAULT 0,
  next_attempt_at timestamptz,
  last_error text,
  created_at timestamptz NOT NULL DEFAULT now(),
  delivered_at timestamptz
);

CREATE TABLE background_jobs (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid REFERENCES tenants(id),
  job_type text NOT NULL,
  payload jsonb NOT NULL,
  status text NOT NULL DEFAULT 'queued' CHECK (status IN ('queued', 'running', 'succeeded', 'failed', 'cancelled')),
  attempts integer NOT NULL DEFAULT 0,
  run_after timestamptz NOT NULL DEFAULT now(),
  locked_until timestamptz,
  error_message text,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_background_jobs_queue ON background_jobs (status, run_after, locked_until);

INSERT INTO permissions (code, description) VALUES
  ('tenant.manage', 'Manage tenant settings'),
  ('stores.manage', 'Manage stores'),
  ('users.manage', 'Manage users'),
  ('roles.manage', 'Manage roles and permissions'),
  ('catalog.read', 'Read catalog'),
  ('catalog.manage', 'Manage catalog'),
  ('inventory.read', 'Read inventory'),
  ('inventory.adjust', 'Adjust inventory'),
  ('sales.create', 'Create sales'),
  ('sales.void', 'Void sales'),
  ('returns.create', 'Create returns'),
  ('cash.open', 'Open cash shift'),
  ('cash.close', 'Close cash shift'),
  ('cash.move', 'Create cash movements'),
  ('reports.read', 'Read reports'),
  ('builder.manage', 'Manage POS Builder'),
  ('updates.manage', 'Manage releases'),
  ('audit.read', 'Read audit events')
ON CONFLICT (code) DO NOTHING;

-- RLS
ALTER TABLE tenant_configs ENABLE ROW LEVEL SECURITY;
ALTER TABLE stores ENABLE ROW LEVEL SECURITY;
ALTER TABLE users ENABLE ROW LEVEL SECURITY;
ALTER TABLE roles ENABLE ROW LEVEL SECURITY;
ALTER TABLE role_permissions ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_roles ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_store_access ENABLE ROW LEVEL SECURITY;
ALTER TABLE refresh_tokens ENABLE ROW LEVEL SECURITY;
ALTER TABLE terminals ENABLE ROW LEVEL SECURITY;
ALTER TABLE enrollment_tokens ENABLE ROW LEVEL SECURITY;
ALTER TABLE terminal_offline_unlock_codes ENABLE ROW LEVEL SECURITY;
ALTER TABLE categories ENABLE ROW LEVEL SECURITY;
ALTER TABLE unit_families ENABLE ROW LEVEL SECURITY;
ALTER TABLE units ENABLE ROW LEVEL SECURITY;
ALTER TABLE products ENABLE ROW LEVEL SECURITY;
ALTER TABLE product_variants ENABLE ROW LEVEL SECURITY;
ALTER TABLE product_barcodes ENABLE ROW LEVEL SECURITY;
ALTER TABLE price_lists ENABLE ROW LEVEL SECURITY;
ALTER TABLE product_prices ENABLE ROW LEVEL SECURITY;
ALTER TABLE modifier_groups ENABLE ROW LEVEL SECURITY;
ALTER TABLE modifiers ENABLE ROW LEVEL SECURITY;
ALTER TABLE product_modifier_groups ENABLE ROW LEVEL SECURITY;
ALTER TABLE recipes ENABLE ROW LEVEL SECURITY;
ALTER TABLE recipe_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE customers ENABLE ROW LEVEL SECURITY;
ALTER TABLE cash_shifts ENABLE ROW LEVEL SECURITY;
ALTER TABLE cash_movements ENABLE ROW LEVEL SECURITY;
ALTER TABLE sales ENABLE ROW LEVEL SECURITY;
ALTER TABLE sale_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE payment_methods ENABLE ROW LEVEL SECURITY;
ALTER TABLE payments ENABLE ROW LEVEL SECURITY;
ALTER TABLE returns ENABLE ROW LEVEL SECURITY;
ALTER TABLE return_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE digital_receipts ENABLE ROW LEVEL SECURITY;
ALTER TABLE inventory_ledger ENABLE ROW LEVEL SECURITY;
ALTER TABLE purchase_orders ENABLE ROW LEVEL SECURITY;
ALTER TABLE purchase_order_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE stock_counts ENABLE ROW LEVEL SECURITY;
ALTER TABLE stock_count_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE discounts ENABLE ROW LEVEL SECURITY;
ALTER TABLE loyalty_accounts ENABLE ROW LEVEL SECURITY;
ALTER TABLE production_orders ENABLE ROW LEVEL SECURITY;
ALTER TABLE production_order_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE idempotency_keys ENABLE ROW LEVEL SECURITY;
ALTER TABLE sync_inbox_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE sync_changes ENABLE ROW LEVEL SECURITY;
ALTER TABLE sync_conflicts ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE builder_projects ENABLE ROW LEVEL SECURITY;
ALTER TABLE builder_builds ENABLE ROW LEVEL SECURITY;
ALTER TABLE webhooks ENABLE ROW LEVEL SECURITY;
ALTER TABLE webhook_deliveries ENABLE ROW LEVEL SECURITY;

DO $$
DECLARE
  table_name text;
BEGIN
  FOREACH table_name IN ARRAY ARRAY[
    'tenant_configs','stores','users','roles','role_permissions','user_roles','user_store_access',
    'refresh_tokens','terminals','enrollment_tokens','terminal_offline_unlock_codes','categories','unit_families','units','products',
    'product_variants','product_barcodes','price_lists','product_prices','modifier_groups','modifiers',
    'product_modifier_groups','recipes','recipe_items','customers','cash_shifts','cash_movements',
    'sales','sale_lines','payment_methods','payments','returns','return_lines','digital_receipts','inventory_ledger',
    'purchase_orders','purchase_order_lines','stock_counts','stock_count_lines','discounts',
    'loyalty_accounts','production_orders','production_order_lines','idempotency_keys',
    'sync_inbox_events','sync_changes','sync_conflicts','audit_events','builder_projects',
    'builder_builds','webhooks','webhook_deliveries'
  ]
  LOOP
    EXECUTE format('CREATE POLICY tenant_isolation_%I ON %I USING (tenant_id = pos.current_tenant_id()) WITH CHECK (tenant_id = pos.current_tenant_id())', table_name, table_name);
  END LOOP;
END $$;

COMMIT;
