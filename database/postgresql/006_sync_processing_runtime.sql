BEGIN;

SET search_path TO pos, public;

ALTER TABLE sync_inbox_events
  DROP CONSTRAINT IF EXISTS sync_inbox_events_status_check;

ALTER TABLE sync_inbox_events
  ADD CONSTRAINT sync_inbox_events_status_check
  CHECK (status IN ('received', 'processing', 'processed', 'duplicate', 'rejected', 'conflict'));

CREATE INDEX IF NOT EXISTS idx_sync_inbox_processing
  ON sync_inbox_events (tenant_id, store_id, terminal_id, status, created_at);

COMMIT;
