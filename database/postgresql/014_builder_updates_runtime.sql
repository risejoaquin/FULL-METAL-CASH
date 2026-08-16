BEGIN;

SET search_path TO pos, public;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_trigger
    WHERE tgname = 'trg_builder_projects_updated_at'
      AND tgrelid = 'pos.builder_projects'::regclass
  ) THEN
    CREATE TRIGGER trg_builder_projects_updated_at
    BEFORE UPDATE ON pos.builder_projects
    FOR EACH ROW EXECUTE FUNCTION pos.touch_updated_at();
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_builder_projects_tenant_updated
  ON pos.builder_projects (tenant_id, updated_at DESC)
  WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_builder_builds_tenant_project_created
  ON pos.builder_builds (tenant_id, project_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_update_releases_check
  ON pos.update_releases (tenant_id, channel, package_type, published_at DESC)
  WHERE revoked_at IS NULL;

INSERT INTO pos.permissions (code, description) VALUES
  ('builder.manage', 'Manage POS Builder projects and generated builds'),
  ('updates.manage', 'Manage POS update channels and releases')
ON CONFLICT (code) DO UPDATE SET description = EXCLUDED.description;

COMMIT;
