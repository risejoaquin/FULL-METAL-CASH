SET search_path TO pos, public;

ALTER TABLE pos.digital_receipts
  ADD COLUMN IF NOT EXISTS receipt_number text,
  ADD COLUMN IF NOT EXISTS issued_at timestamptz,
  ADD COLUMN IF NOT EXISTS last_sent_at timestamptz,
  ADD COLUMN IF NOT EXISTS last_sent_email text,
  ADD COLUMN IF NOT EXISTS send_count integer NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS metadata jsonb NOT NULL DEFAULT '{}'::jsonb;

UPDATE pos.digital_receipts dr
SET receipt_number = 'SP-' || to_char(s.occurred_at AT TIME ZONE 'UTC', 'YYYYMMDD') || '-' || upper(substr(replace(s.id::text, '-', ''), 1, 8))
FROM pos.sales s
WHERE s.tenant_id = dr.tenant_id
  AND s.id = dr.sale_id
  AND dr.receipt_number IS NULL;

UPDATE pos.digital_receipts
SET issued_at = created_at
WHERE issued_at IS NULL;

ALTER TABLE pos.digital_receipts
  ALTER COLUMN receipt_number SET NOT NULL,
  ALTER COLUMN issued_at SET NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_digital_receipts_tenant_receipt_number
ON pos.digital_receipts (tenant_id, receipt_number);

CREATE INDEX IF NOT EXISTS idx_digital_receipts_sale_status
ON pos.digital_receipts (tenant_id, sale_id, status);
