BEGIN;

SET search_path TO pos, public;

ALTER TABLE sync_inbox_events
  ADD COLUMN IF NOT EXISTS batch_id uuid,
  ADD COLUMN IF NOT EXISTS schema_version integer NOT NULL DEFAULT 1,
  ADD COLUMN IF NOT EXISTS sequence_number integer;

CREATE INDEX IF NOT EXISTS idx_sync_inbox_batch
  ON sync_inbox_events (tenant_id, terminal_id, batch_id, sequence_number)
  WHERE batch_id IS NOT NULL;

COMMIT;
