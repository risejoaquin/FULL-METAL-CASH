BEGIN;

SET search_path TO pos, public;

ALTER TABLE sync_inbox_events
  DROP CONSTRAINT IF EXISTS sync_inbox_events_status_check;

ALTER TABLE sync_inbox_events
  ADD CONSTRAINT sync_inbox_events_status_check
  CHECK (status IN ('received', 'processing', 'processed', 'duplicate', 'rejected', 'retry_pending', 'conflict', 'dead_letter'));

ALTER TABLE sync_inbox_events
  ADD COLUMN IF NOT EXISTS attempts integer NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS max_attempts integer NOT NULL DEFAULT 3,
  ADD COLUMN IF NOT EXISTS last_attempt_at timestamptz,
  ADD COLUMN IF NOT EXISTS next_retry_at timestamptz,
  ADD COLUMN IF NOT EXISTS dead_lettered_at timestamptz,
  ADD COLUMN IF NOT EXISTS conflict_id uuid REFERENCES sync_conflicts(id);

ALTER TABLE sync_conflicts
  DROP CONSTRAINT IF EXISTS sync_conflicts_status_check;

ALTER TABLE sync_conflicts
  ADD CONSTRAINT sync_conflicts_status_check
  CHECK (status IN ('pending', 'resolved', 'ignored'));

ALTER TABLE sync_conflicts
  ADD COLUMN IF NOT EXISTS resolved_by_user_id uuid REFERENCES users(id),
  ADD COLUMN IF NOT EXISTS resolution_note text,
  ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();

CREATE INDEX IF NOT EXISTS idx_sync_inbox_retry
  ON sync_inbox_events (tenant_id, store_id, terminal_id, status, next_retry_at, created_at);

CREATE INDEX IF NOT EXISTS idx_sync_inbox_dead_letter
  ON sync_inbox_events (tenant_id, store_id, terminal_id, dead_lettered_at)
  WHERE status = 'dead_letter';

CREATE INDEX IF NOT EXISTS idx_sync_inbox_conflict
  ON sync_inbox_events (tenant_id, conflict_id)
  WHERE conflict_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_sync_conflicts_tenant_status
  ON sync_conflicts (tenant_id, status, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_sync_conflicts_entity
  ON sync_conflicts (tenant_id, entity_type, entity_id, status);

COMMIT;
