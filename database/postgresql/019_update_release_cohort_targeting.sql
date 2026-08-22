BEGIN;
SET search_path TO pos, public;

CREATE TABLE IF NOT EXISTS pos.update_release_targets (
  release_id uuid NOT NULL REFERENCES pos.update_releases(id) ON DELETE CASCADE,
  tenant_id uuid NOT NULL REFERENCES pos.tenants(id),
  terminal_id uuid NOT NULL REFERENCES pos.terminals(id),
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (release_id, terminal_id)
);

CREATE INDEX IF NOT EXISTS idx_update_release_targets_tenant_terminal
  ON pos.update_release_targets (tenant_id, terminal_id, release_id);

ALTER TABLE pos.update_release_targets ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS update_release_targets_tenant_isolation ON pos.update_release_targets;
CREATE POLICY update_release_targets_tenant_isolation ON pos.update_release_targets
  USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
  WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

COMMIT;
