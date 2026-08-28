\set tenant_uuid :tenant_id
\set max_stores_value :max_stores
\set max_concurrent_terminals_value :max_concurrent_terminals
\set allowed_conflicts_value :allowed_existing_sync_conflicts
\set allowed_dead_letters_value :allowed_dead_letters
\set allowed_waiting_connections_value :allowed_waiting_connections
WITH required_tables AS (
  SELECT unnest(ARRAY['pos.tenants','pos.stores','pos.users','pos.terminals','pos.products','pos.payments','pos.sales','pos.cash_shifts','pos.inventory_ledger','pos.sync_inbox_events','pos.sync_conflicts','pos.audit_events','pos.update_releases','pos.digital_receipts']) AS table_name
), table_presence AS (
  SELECT table_name, to_regclass(table_name) IS NOT NULL AS present FROM required_tables
), tenant_state AS (
  SELECT count(*) FILTER (WHERE status = 'active' AND deleted_at IS NULL)::bigint AS active_tenant_count FROM pos.tenants WHERE id = :'tenant_uuid'::uuid
), rollout_scope AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.stores WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'active' AND deleted_at IS NULL) AS active_store_count,
    (SELECT count(*)::bigint FROM pos.terminals WHERE tenant_id = :'tenant_uuid'::uuid AND status IN ('active','pending') AND deleted_at IS NULL) AS available_terminal_count,
    (SELECT count(*)::bigint FROM pos.cash_shifts WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'open') AS open_shift_count,
    (SELECT count(*)::bigint FROM pos.update_releases WHERE tenant_id = :'tenant_uuid'::uuid AND channel = 'stable' AND revoked_at IS NULL) AS active_stable_release_count,
    :'max_stores_value'::int AS max_stores,
    :'max_concurrent_terminals_value'::int AS max_concurrent_terminals
), sync_integrity AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND schema_version <> 4) AS legacy_schema_event_count,
    (SELECT count(*)::bigint FROM pos.sync_conflicts WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'pending') AS pending_conflict_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'retry_pending') AS retry_pending_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'dead_letter') AS dead_letter_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events e WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'processing' AND ((COALESCE(to_jsonb(e)->>'updated_at', to_jsonb(e)->>'created_at'))::timestamptz) < now() - interval '15 minutes') AS stale_processing_count,
    :'allowed_conflicts_value'::int AS allowed_existing_sync_conflict_count,
    :'allowed_dead_letters_value'::int AS allowed_dead_letter_count
), negative_stock AS (
  SELECT store_id, product_id, variant_id, sum(quantity_delta) AS quantity_on_hand
  FROM pos.inventory_ledger
  WHERE tenant_id = :'tenant_uuid'::uuid
  GROUP BY store_id, product_id, variant_id
  HAVING sum(quantity_delta) < 0
), negative_stock_detail AS (
  SELECT ns.store_id, ns.product_id, ns.variant_id, p.sku, p.name, u.id AS unit_id, u.code AS unit_code, ns.quantity_on_hand,
         abs(ns.quantity_on_hand) AS adjustment_quantity_needed
  FROM negative_stock ns
  JOIN pos.products p ON p.id = ns.product_id
  JOIN pos.units u ON u.id = (SELECT il.unit_id FROM pos.inventory_ledger il WHERE il.tenant_id = :'tenant_uuid'::uuid AND il.store_id = ns.store_id AND il.product_id = ns.product_id AND (il.variant_id IS NOT DISTINCT FROM ns.variant_id) ORDER BY il.created_at DESC LIMIT 1)
  ORDER BY ns.quantity_on_hand ASC
), financial_integrity AS (
  SELECT
    (SELECT count(*)::bigint FROM (SELECT tenant_id, terminal_id, local_sale_id FROM pos.sales WHERE tenant_id = :'tenant_uuid'::uuid GROUP BY tenant_id, terminal_id, local_sale_id HAVING count(*) > 1) d) AS duplicate_local_sale_count,
    (SELECT count(*)::bigint FROM pos.payments WHERE tenant_id = :'tenant_uuid'::uuid AND amount_cents < 0) AS negative_payment_count,
    (SELECT count(*)::bigint FROM negative_stock) AS negative_stock_item_count
), db_pressure AS (
  SELECT count(*) FILTER (WHERE wait_event IS NOT NULL)::bigint AS waiting_connection_count,
         count(*) FILTER (WHERE state = 'active' AND now() - query_start > interval '30 seconds')::bigint AS long_running_query_count,
         count(*)::bigint AS observed_connection_count,
         :'allowed_waiting_connections_value'::int AS allowed_waiting_connection_count
  FROM pg_stat_activity WHERE datname = current_database()
), rls AS (
  SELECT count(*) FILTER (WHERE c.relrowsecurity = true)::bigint AS rls_enabled_table_count,
         count(*) FILTER (WHERE c.relrowsecurity = false)::bigint AS rls_missing_table_count
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
  WHERE n.nspname = 'pos' AND c.relkind = 'r'
    AND EXISTS (SELECT 1 FROM information_schema.columns col WHERE col.table_schema = 'pos' AND col.table_name = c.relname AND col.column_name = 'tenant_id')
), monitoring_activity AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.sales WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'completed' AND occurred_at >= now() - interval '24 hours') AS completed_sales_24h,
    (SELECT count(*)::bigint FROM pos.payments WHERE tenant_id = :'tenant_uuid'::uuid AND created_at >= now() - interval '24 hours') AS payments_24h,
    (SELECT count(*)::bigint FROM pos.digital_receipts WHERE tenant_id = :'tenant_uuid'::uuid AND issued_at >= now() - interval '24 hours') AS receipts_issued_24h,
    (SELECT count(*)::bigint FROM pos.audit_events WHERE tenant_id = :'tenant_uuid'::uuid AND occurred_at >= now() - interval '24 hours') AS audit_events_24h
), payload AS (
  SELECT jsonb_build_object(
    'schemaVersion', 4,
    'syncContract', 'schema_version_4',
    'tenantId', :'tenant_uuid',
    'requiredTablesPresent', NOT EXISTS (SELECT 1 FROM table_presence WHERE present = false),
    'missingRequiredTables', COALESCE((SELECT jsonb_agg(table_name) FROM table_presence WHERE present = false), '[]'::jsonb),
    'tenantState', (SELECT row_to_json(tenant_state) FROM tenant_state),
    'rolloutScope', (SELECT row_to_json(rollout_scope) FROM rollout_scope),
    'syncIntegrity', (SELECT row_to_json(sync_integrity) FROM sync_integrity),
    'financialIntegrity', (SELECT row_to_json(financial_integrity) FROM financial_integrity),
    'negativeStock', jsonb_build_object('count', (SELECT count(*) FROM negative_stock_detail), 'items', COALESCE((SELECT jsonb_agg(row_to_json(negative_stock_detail)) FROM negative_stock_detail), '[]'::jsonb)),
    'databasePressure', (SELECT row_to_json(db_pressure) FROM db_pressure),
    'rls', (SELECT row_to_json(rls) FROM rls),
    'monitoringActivity', (SELECT row_to_json(monitoring_activity) FROM monitoring_activity),
    'generalAvailabilityActivated', false,
    'publicGeneralAvailabilityActivated', false,
    'generatedAt', now()
  ) AS result
)
SELECT 'LGA01_JSON:' || result::text FROM payload;
