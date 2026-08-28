\set tenant_uuid :tenant_id
WITH required_tables AS (
  SELECT unnest(ARRAY[
    'pos.audit_events',
    'pos.sync_inbox_events',
    'pos.sync_conflicts',
    'pos.sales',
    'pos.payments',
    'pos.inventory_policies',
    'pos.inventory_ledger'
  ]) AS table_name
), table_presence AS (
  SELECT table_name, to_regclass(table_name) IS NOT NULL AS present
  FROM required_tables
), sync_counts AS (
  SELECT
    count(*) FILTER (WHERE status = 'retry_pending')::bigint AS retry_pending_count,
    count(*) FILTER (WHERE status = 'dead_letter')::bigint AS dead_letter_count,
    count(*) FILTER (WHERE schema_version <> 4)::bigint AS legacy_schema_event_count
  FROM pos.sync_inbox_events
  WHERE tenant_id = :'tenant_uuid'::uuid
), conflict_counts AS (
  SELECT count(*) FILTER (WHERE status = 'pending')::bigint AS pending_conflict_count
  FROM pos.sync_conflicts
  WHERE tenant_id = :'tenant_uuid'::uuid
), audit_counts AS (
  SELECT
    count(*) FILTER (WHERE occurred_at >= now() - interval '24 hours')::bigint AS audit_events_last_24h,
    max(occurred_at) AS last_audit_event_at
  FROM pos.audit_events
  WHERE tenant_id = :'tenant_uuid'::uuid
), financial_counts AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.payments WHERE tenant_id = :'tenant_uuid'::uuid AND status IN ('failed','declined') AND created_at >= now() - interval '24 hours') AS failed_or_declined_payments_last_24h,
    (SELECT count(*)::bigint FROM (
      SELECT tenant_id, local_sale_id FROM pos.sales WHERE tenant_id = :'tenant_uuid'::uuid AND local_sale_id IS NOT NULL GROUP BY tenant_id, local_sale_id HAVING count(*) > 1
    ) d) AS duplicate_local_sale_count
), db_pressure AS (
  SELECT
    count(*) FILTER (WHERE wait_event IS NOT NULL)::bigint AS waiting_connection_count,
    count(*) FILTER (WHERE state = 'active' AND now() - query_start > interval '30 seconds')::bigint AS long_running_query_count,
    count(*)::bigint AS observed_connection_count
  FROM pg_stat_activity
  WHERE datname = current_database()
), rls AS (
  SELECT
    count(*) FILTER (WHERE c.relrowsecurity = true)::bigint AS rls_enabled_table_count,
    count(*) FILTER (WHERE c.relrowsecurity = false)::bigint AS rls_missing_table_count
  FROM pg_class c
  JOIN pg_namespace n ON n.oid = c.relnamespace
  WHERE n.nspname = 'pos'
    AND c.relkind = 'r'
    AND EXISTS (
      SELECT 1
      FROM information_schema.columns col
      WHERE col.table_schema = 'pos'
        AND col.table_name = c.relname
        AND col.column_name = 'tenant_id'
    )
), payload AS (
  SELECT jsonb_build_object(
    'schemaVersion', 4,
    'syncContract', 'schema_version_4',
    'tenantId', :'tenant_uuid',
    'requiredTablesPresent', NOT EXISTS (SELECT 1 FROM table_presence WHERE present = false),
    'missingRequiredTables', COALESCE((SELECT jsonb_agg(table_name) FROM table_presence WHERE present = false), '[]'::jsonb),
    'sync', (SELECT row_to_json(sync_counts) FROM sync_counts),
    'conflicts', (SELECT row_to_json(conflict_counts) FROM conflict_counts),
    'audit', (SELECT row_to_json(audit_counts) FROM audit_counts),
    'financial', (SELECT row_to_json(financial_counts) FROM financial_counts),
    'databasePressure', (SELECT row_to_json(db_pressure) FROM db_pressure),
    'rls', (SELECT row_to_json(rls) FROM rls),
    'generatedAt', now()
  ) AS result
)
SELECT 'GA10_JSON:' || result::text FROM payload;
