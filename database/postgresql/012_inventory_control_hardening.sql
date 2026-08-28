BEGIN;

SET search_path TO pos, public;

CREATE TABLE IF NOT EXISTS inventory_policies (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  store_id uuid REFERENCES stores(id),
  store_id_key uuid GENERATED ALWAYS AS (COALESCE(store_id, '00000000-0000-0000-0000-000000000000'::uuid)) STORED,
  allow_negative_stock boolean NOT NULL DEFAULT true,
  enforce_at_sale boolean NOT NULL DEFAULT true,
  offline_sale_behavior text NOT NULL DEFAULT 'allow_and_reconcile' CHECK (offline_sale_behavior IN ('allow_and_reconcile', 'warn_and_reconcile', 'block_when_online')),
  low_stock_alerts_enabled boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, store_id_key)
);

CREATE INDEX IF NOT EXISTS idx_inventory_policies_tenant_store
ON inventory_policies (tenant_id, store_id);

CREATE TABLE IF NOT EXISTS inventory_low_stock_thresholds (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  store_id uuid NOT NULL REFERENCES stores(id),
  product_id uuid NOT NULL REFERENCES products(id),
  variant_id uuid REFERENCES product_variants(id),
  unit_id uuid NOT NULL REFERENCES units(id),
  reorder_point numeric(18,4) NOT NULL CHECK (reorder_point >= 0),
  reorder_quantity numeric(18,4) NOT NULL DEFAULT 0 CHECK (reorder_quantity >= 0),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, store_id, product_id, variant_id, unit_id)
);

CREATE INDEX IF NOT EXISTS idx_inventory_low_stock_thresholds_lookup
ON inventory_low_stock_thresholds (tenant_id, store_id, product_id, variant_id, unit_id);

CREATE TABLE IF NOT EXISTS inventory_counts (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  store_id uuid NOT NULL REFERENCES stores(id),
  terminal_id uuid REFERENCES terminals(id),
  local_count_id uuid NOT NULL,
  status text NOT NULL DEFAULT 'completed' CHECK (status IN ('completed', 'voided')),
  reason text NOT NULL,
  created_by_user_id uuid NOT NULL REFERENCES users(id),
  occurred_at timestamptz NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, local_count_id)
);

CREATE TABLE IF NOT EXISTS inventory_count_lines (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  count_id uuid NOT NULL REFERENCES inventory_counts(id),
  product_id uuid NOT NULL REFERENCES products(id),
  variant_id uuid REFERENCES product_variants(id),
  unit_id uuid NOT NULL REFERENCES units(id),
  previous_quantity numeric(18,4) NOT NULL,
  counted_quantity numeric(18,4) NOT NULL CHECK (counted_quantity >= 0),
  adjustment_delta numeric(18,4) NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_inventory_counts_tenant_store_occurred
ON inventory_counts (tenant_id, store_id, occurred_at DESC);

CREATE INDEX IF NOT EXISTS idx_inventory_count_lines_count
ON inventory_count_lines (tenant_id, count_id);

CREATE TABLE IF NOT EXISTS inventory_transfers (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  from_store_id uuid NOT NULL REFERENCES stores(id),
  to_store_id uuid NOT NULL REFERENCES stores(id),
  local_transfer_id uuid NOT NULL,
  status text NOT NULL DEFAULT 'completed' CHECK (status IN ('completed', 'voided')),
  reason text NOT NULL,
  created_by_user_id uuid NOT NULL REFERENCES users(id),
  occurred_at timestamptz NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, local_transfer_id),
  CHECK (from_store_id <> to_store_id)
);

CREATE TABLE IF NOT EXISTS inventory_transfer_lines (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  transfer_id uuid NOT NULL REFERENCES inventory_transfers(id),
  product_id uuid NOT NULL REFERENCES products(id),
  variant_id uuid REFERENCES product_variants(id),
  unit_id uuid NOT NULL REFERENCES units(id),
  quantity numeric(18,4) NOT NULL CHECK (quantity > 0),
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_inventory_transfers_tenant_store_occurred
ON inventory_transfers (tenant_id, from_store_id, to_store_id, occurred_at DESC);

CREATE INDEX IF NOT EXISTS idx_inventory_transfer_lines_transfer
ON inventory_transfer_lines (tenant_id, transfer_id);

INSERT INTO inventory_policies (tenant_id, store_id, allow_negative_stock, enforce_at_sale, offline_sale_behavior, low_stock_alerts_enabled)
SELECT t.id, NULL, true, true, 'allow_and_reconcile', true
FROM tenants t
ON CONFLICT (tenant_id, store_id_key) DO NOTHING;

INSERT INTO inventory_low_stock_thresholds (tenant_id, store_id, product_id, variant_id, unit_id, reorder_point, reorder_quantity)
SELECT p.tenant_id, s.id, p.id, NULL, p.inventory_unit_id, 20, 50
FROM products p
JOIN stores s ON s.tenant_id = p.tenant_id
WHERE p.is_stock_tracked = true
  AND p.inventory_unit_id IS NOT NULL
  AND p.status = 'active'
  AND p.deleted_at IS NULL
ON CONFLICT (tenant_id, store_id, product_id, variant_id, unit_id) DO NOTHING;

COMMIT;
