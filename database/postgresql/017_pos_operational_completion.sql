BEGIN;
SET search_path TO pos, public;

ALTER TABLE cash_movements
  ADD COLUMN IF NOT EXISTS occurred_at timestamptz,
  ADD COLUMN IF NOT EXISTS local_occurred_at timestamptz,
  ADD COLUMN IF NOT EXISTS metadata jsonb NOT NULL DEFAULT '{}'::jsonb;

UPDATE cash_movements
SET occurred_at = created_at
WHERE occurred_at IS NULL;

ALTER TABLE cash_movements
  ALTER COLUMN occurred_at SET DEFAULT now(),
  ALTER COLUMN occurred_at SET NOT NULL;

CREATE INDEX IF NOT EXISTS idx_cash_movements_shift_occurred
ON cash_movements (tenant_id, cash_shift_id, occurred_at DESC);

CREATE INDEX IF NOT EXISTS idx_sales_cash_shift_status
ON sales (tenant_id, cash_shift_id, status, occurred_at DESC)
WHERE cash_shift_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_returns_cash_shift_status
ON returns (tenant_id, cash_shift_id, status, occurred_at DESC)
WHERE cash_shift_id IS NOT NULL;

INSERT INTO permissions (code, description)
VALUES
  ('reports.cash_shift_summary', 'Read operational cash-shift close summaries')
ON CONFLICT (code) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO role_permissions (tenant_id, role_id, permission_code)
SELECT r.tenant_id, r.id, 'reports.cash_shift_summary'
FROM roles r
WHERE r.code IN ('owner', 'manager')
ON CONFLICT DO NOTHING;

COMMIT;
