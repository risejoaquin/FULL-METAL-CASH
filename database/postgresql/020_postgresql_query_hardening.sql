BEGIN;

SET search_path TO pos, public;

-- V1.1-04: targeted indexes for the highest-volume operational read paths.
CREATE INDEX IF NOT EXISTS idx_sales_tenant_terminal_occurred_active
  ON sales (tenant_id, terminal_id, occurred_at DESC, created_at DESC)
  WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_sales_tenant_status_occurred_active
  ON sales (tenant_id, status, occurred_at DESC, created_at DESC)
  WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_payments_tenant_status_created
  ON payments (tenant_id, status, created_at DESC);

COMMIT;
