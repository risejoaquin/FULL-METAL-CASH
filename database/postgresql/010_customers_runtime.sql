BEGIN;

SET search_path TO pos, public;

CREATE INDEX IF NOT EXISTS idx_customers_tenant_status_updated
  ON customers (tenant_id, status, updated_at DESC)
  WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_sales_customer_history
  ON sales (tenant_id, customer_id, occurred_at DESC, created_at DESC)
  WHERE customer_id IS NOT NULL AND deleted_at IS NULL;

COMMIT;
