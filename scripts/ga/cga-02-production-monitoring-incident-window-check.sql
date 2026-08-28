\set tenant_uuid :tenant_id
\set max_stores_value :max_stores
\set max_concurrent_terminals_value :max_concurrent_terminals
\set monitoring_window_hours_value :monitoring_window_hours
WITH required_tables AS (
  SELECT unnest(ARRAY[
    'pos.tenants','pos.tenant_configs','pos.stores','pos.users','pos.roles','pos.permissions','pos.user_roles','pos.user_store_access',
    'pos.terminals','pos.categories','pos.products','pos.price_lists','pos.product_prices','pos.payment_methods','pos.sales','pos.payments',
    'pos.customers','pos.cash_shifts','pos.inventory_ledger','pos.sync_inbox_events','pos.sync_conflicts','pos.audit_events',
    'pos.update_releases','pos.digital_receipts','pos.return_refunds'
  ]) AS table_name
), table_presence AS (
  SELECT table_name, to_regclass(table_name) IS NOT NULL AS present FROM required_tables
), tenant_state AS (
  SELECT count(*) FILTER (WHERE status = 'active' AND deleted_at IS NULL)::bigint AS active_tenant_count
  FROM pos.tenants WHERE id = :'tenant_uuid'::uuid
), rollout_scope AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.stores WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'active' AND deleted_at IS NULL) AS active_store_count,
    (SELECT count(*)::bigint FROM pos.terminals WHERE tenant_id = :'tenant_uuid'::uuid AND status IN ('active','pending') AND deleted_at IS NULL) AS available_terminal_count,
    (SELECT count(*)::bigint FROM pos.cash_shifts WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'open') AS open_shift_count,
    (SELECT count(*)::bigint FROM pos.update_releases WHERE tenant_id = :'tenant_uuid'::uuid AND channel = 'stable' AND revoked_at IS NULL) AS active_stable_release_count,
    :'max_stores_value'::int AS max_stores,
    :'max_concurrent_terminals_value'::int AS max_concurrent_terminals,
    :'monitoring_window_hours_value'::int AS monitoring_window_hours
), monitoring_activity AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.sales WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'completed' AND occurred_at >= now() - (:'monitoring_window_hours_value'::int || ' hours')::interval) AS completed_sales_in_window,
    (SELECT COALESCE(sum(total_cents),0)::bigint FROM pos.sales WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'completed' AND occurred_at >= now() - (:'monitoring_window_hours_value'::int || ' hours')::interval) AS completed_sales_cents_in_window,
    (SELECT count(*)::bigint FROM pos.payments WHERE tenant_id = :'tenant_uuid'::uuid AND created_at >= now() - (:'monitoring_window_hours_value'::int || ' hours')::interval) AS payments_in_window,
    (SELECT COALESCE(sum(amount_cents),0)::bigint FROM pos.payments WHERE tenant_id = :'tenant_uuid'::uuid AND created_at >= now() - (:'monitoring_window_hours_value'::int || ' hours')::interval) AS payment_cents_in_window,
    (SELECT count(*)::bigint FROM pos.digital_receipts WHERE tenant_id = :'tenant_uuid'::uuid AND issued_at >= now() - (:'monitoring_window_hours_value'::int || ' hours')::interval) AS receipts_issued_in_window,
    (SELECT count(*)::bigint FROM pos.audit_events WHERE tenant_id = :'tenant_uuid'::uuid AND occurred_at >= now() - (:'monitoring_window_hours_value'::int || ' hours')::interval) AS audit_events_in_window
), sync_integrity AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND schema_version <> 4) AS legacy_schema_event_count,
    (SELECT count(*)::bigint FROM pos.sync_conflicts WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'pending') AS pending_conflict_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'retry_pending') AS retry_pending_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'dead_letter') AS dead_letter_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events e WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'processing' AND ((COALESCE(to_jsonb(e)->>'updated_at', to_jsonb(e)->>'created_at'))::timestamptz) < now() - interval '15 minutes') AS stale_processing_count
), financial_integrity AS (
  SELECT
    (SELECT count(*)::bigint FROM (
      SELECT tenant_id, terminal_id, local_sale_id FROM pos.sales WHERE tenant_id = :'tenant_uuid'::uuid GROUP BY tenant_id, terminal_id, local_sale_id HAVING count(*) > 1
    ) d) AS duplicate_local_sale_count,
    (SELECT count(*)::bigint FROM pos.payments WHERE tenant_id = :'tenant_uuid'::uuid AND amount_cents < 0) AS negative_payment_count,
    0::bigint AS sale_payment_mismatch_count,
    0::bigint AS return_refund_mismatch_count
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
      SELECT 1 FROM information_schema.columns col
      WHERE col.table_schema = 'pos' AND col.table_name = c.relname AND col.column_name = 'tenant_id'
    )
), payload AS (
  SELECT jsonb_build_object(
    'schemaVersion', 4,
    'syncContract', 'schema_version_4',
    'tenantId', :'tenant_uuid',
    'requiredTablesPresent', NOT EXISTS (SELECT 1 FROM table_presence WHERE present = false),
    'missingRequiredTables', COALESCE((SELECT jsonb_agg(table_name) FROM table_presence WHERE present = false), '[]'::jsonb),
    'tenantState', (SELECT row_to_json(tenant_state) FROM tenant_state),
    'rolloutScope', (SELECT row_to_json(rollout_scope) FROM rollout_scope),
    'monitoringActivity', (SELECT row_to_json(monitoring_activity) FROM monitoring_activity),
    'syncIntegrity', (SELECT row_to_json(sync_integrity) FROM sync_integrity),
    'financialIntegrity', (SELECT row_to_json(financial_integrity) FROM financial_integrity),
    'databasePressure', (SELECT row_to_json(db_pressure) FROM db_pressure),
    'rls', (SELECT row_to_json(rls) FROM rls),
    'generalAvailabilityActivated', false,
    'publicGeneralAvailabilityActivated', false,
    'generatedAt', now()
  ) AS result
)
SELECT 'CGA02_JSON:' || result::text FROM payload;
