BEGIN;

SET search_path TO pos, extensions, public;

CREATE TABLE IF NOT EXISTS production_bootstrap_runs (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  idempotency_key text NOT NULL UNIQUE,
  request_hash text NOT NULL,
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  admin_user_id uuid NOT NULL REFERENCES users(id),
  store_id uuid NOT NULL REFERENCES stores(id),
  status text NOT NULL DEFAULT 'completed' CHECK (status IN ('completed', 'failed')),
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_production_bootstrap_runs_tenant_created
ON production_bootstrap_runs (tenant_id, created_at DESC);

INSERT INTO permissions (code, description)
VALUES
  ('provisioning.bootstrap', 'Bootstrap production tenant, admin user, and initial store')
ON CONFLICT (code) DO UPDATE SET description = EXCLUDED.description;

COMMIT;
