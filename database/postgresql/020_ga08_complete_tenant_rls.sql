\set ON_ERROR_STOP on
BEGIN;
SET search_path TO pos, public;

-- GA-08 closes every known tenant-scoped table that historically missed the
-- initial RLS enablement block. This migration is additive and idempotent.
ALTER TABLE IF EXISTS return_refunds ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS inventory_policies ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS inventory_low_stock_thresholds ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS inventory_counts ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS inventory_count_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS inventory_transfers ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS inventory_transfer_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS production_bootstrap_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS update_releases ENABLE ROW LEVEL SECURITY;
ALTER TABLE IF EXISTS background_jobs ENABLE ROW LEVEL SECURITY;

DO $$
DECLARE
  table_name text;
BEGIN
  FOREACH table_name IN ARRAY ARRAY[
    'return_refunds',
    'inventory_policies',
    'inventory_low_stock_thresholds',
    'inventory_counts',
    'inventory_count_lines',
    'inventory_transfers',
    'inventory_transfer_lines',
    'production_bootstrap_runs'
  ]
  LOOP
    IF to_regclass(format('pos.%I', table_name)) IS NOT NULL
       AND NOT EXISTS (
         SELECT 1
         FROM pg_policies
         WHERE schemaname='pos'
           AND tablename=table_name
           AND policyname=('tenant_isolation_' || table_name)
       ) THEN
      EXECUTE format(
        'CREATE POLICY %I ON %I USING (tenant_id = pos.current_tenant_id()) WITH CHECK (tenant_id = pos.current_tenant_id())',
        'tenant_isolation_' || table_name,
        table_name
      );
    END IF;
  END LOOP;

  -- update_releases and background_jobs intentionally support global rows
  -- (tenant_id IS NULL). RLS must hide foreign tenant rows without breaking
  -- those pre-existing global contracts.
  IF to_regclass('pos.update_releases') IS NOT NULL AND NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE schemaname='pos' AND tablename='update_releases' AND policyname='tenant_isolation_update_releases') THEN
    CREATE POLICY tenant_isolation_update_releases ON update_releases
      USING (tenant_id IS NULL OR tenant_id = pos.current_tenant_id())
      WITH CHECK (tenant_id IS NULL OR tenant_id = pos.current_tenant_id());
  END IF;

  IF to_regclass('pos.background_jobs') IS NOT NULL AND NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE schemaname='pos' AND tablename='background_jobs' AND policyname='tenant_isolation_background_jobs') THEN
    CREATE POLICY tenant_isolation_background_jobs ON background_jobs
      USING (tenant_id IS NULL OR tenant_id = pos.current_tenant_id())
      WITH CHECK (tenant_id IS NULL OR tenant_id = pos.current_tenant_id());
  END IF;
END $$;
COMMIT;
