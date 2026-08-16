BEGIN;

SET search_path TO pos, public;

ALTER TABLE returns
  ADD COLUMN IF NOT EXISTS cash_shift_id uuid REFERENCES cash_shifts(id),
  ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'completed',
  ADD COLUMN IF NOT EXISTS refund_cents bigint NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS metadata jsonb NOT NULL DEFAULT '{}'::jsonb;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'ck_returns_status'
      AND conrelid = 'pos.returns'::regclass
  ) THEN
    ALTER TABLE returns
      ADD CONSTRAINT ck_returns_status CHECK (status IN ('completed', 'cancelled'));
  END IF;
END $$;

UPDATE returns r
SET cash_shift_id = s.cash_shift_id
FROM sales s
WHERE r.tenant_id = s.tenant_id
  AND r.sale_id = s.id
  AND r.cash_shift_id IS NULL
  AND s.cash_shift_id IS NOT NULL;

UPDATE returns
SET refund_cents = total_cents
WHERE refund_cents = 0;

CREATE TABLE IF NOT EXISTS return_refunds (
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

CREATE INDEX IF NOT EXISTS idx_returns_tenant_sale
ON returns (tenant_id, sale_id, occurred_at DESC);

CREATE INDEX IF NOT EXISTS idx_returns_tenant_occurred
ON returns (tenant_id, occurred_at DESC);

CREATE INDEX IF NOT EXISTS idx_return_lines_sale_line
ON return_lines (tenant_id, sale_line_id);

CREATE INDEX IF NOT EXISTS idx_return_refunds_return
ON return_refunds (tenant_id, return_id, created_at);

COMMIT;
