BEGIN;

SET search_path TO pos, public;

ALTER TABLE discounts
  ADD COLUMN IF NOT EXISTS store_id uuid REFERENCES stores(id),
  ADD COLUMN IF NOT EXISTS category_id uuid REFERENCES categories(id),
  ADD COLUMN IF NOT EXISTS product_id uuid REFERENCES products(id);

ALTER TABLE discounts
  DROP CONSTRAINT IF EXISTS discounts_discount_type_check;

ALTER TABLE discounts
  ADD CONSTRAINT discounts_discount_type_check
  CHECK (discount_type IN ('percentage', 'fixed_amount'));

ALTER TABLE discounts
  DROP CONSTRAINT IF EXISTS discounts_value_check;

ALTER TABLE discounts
  ADD CONSTRAINT discounts_value_check
  CHECK (
    (discount_type = 'percentage' AND value > 0 AND value <= 100)
    OR
    (discount_type = 'fixed_amount' AND value > 0)
  );

ALTER TABLE discounts
  DROP CONSTRAINT IF EXISTS discounts_date_range_check;

ALTER TABLE discounts
  ADD CONSTRAINT discounts_date_range_check
  CHECK (starts_at IS NULL OR ends_at IS NULL OR starts_at <= ends_at);

CREATE INDEX IF NOT EXISTS idx_discounts_tenant_status
  ON discounts (tenant_id, status, starts_at, ends_at)
  WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_discounts_scope
  ON discounts (tenant_id, store_id, category_id, product_id, status)
  WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_discounts_search
  ON discounts USING gin (to_tsvector('simple', coalesce(name, '') || ' ' || coalesce(code, '')));

COMMIT;
