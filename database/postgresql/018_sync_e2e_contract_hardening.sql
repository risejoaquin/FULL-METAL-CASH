BEGIN;

SET search_path TO pos, public;

ALTER TABLE sync_inbox_events
  ADD COLUMN IF NOT EXISTS replayed_at timestamptz,
  ADD COLUMN IF NOT EXISTS replay_reason text;

CREATE INDEX IF NOT EXISTS idx_sync_inbox_tenant_status_created
  ON sync_inbox_events (tenant_id, status, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_sync_inbox_terminal_status_created
  ON sync_inbox_events (tenant_id, terminal_id, status, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_sync_inbox_dead_letter_ops
  ON sync_inbox_events (tenant_id, dead_lettered_at DESC)
  WHERE status = 'dead_letter';

CREATE INDEX IF NOT EXISTS idx_sync_changes_tenant_cursor
  ON sync_changes (tenant_id, changed_at, id);

INSERT INTO permissions (code, description) VALUES
  ('sync.conflicts.read', 'Read sync conflicts, runtime status, contract diagnostics and dead-letter diagnostics'),
  ('sync.conflicts.resolve', 'Resolve sync conflicts and schedule dead-letter retries')
ON CONFLICT (code) DO UPDATE SET description = EXCLUDED.description;

COMMIT;
