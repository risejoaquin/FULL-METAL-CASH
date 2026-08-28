\set tenant_uuid :tenant_id
\set max_stores_value :max_stores
\set allowed_conflicts_value :allowed_existing_sync_conflicts
\set allowed_dead_letters_value :allowed_dead_letters
\set allowed_waiting_connections_value :allowed_waiting_connections
WITH cfg AS (
  SELECT feature_flags, version, updated_at FROM pos.tenant_configs WHERE tenant_id = :'tenant_uuid'::uuid
), required_tables AS (
  SELECT unnest(ARRAY['pos.tenants','pos.stores','pos.users','pos.terminals','pos.products','pos.payments','pos.sales','pos.cash_shifts','pos.inventory_ledger','pos.sync_inbox_events','pos.sync_conflicts','pos.audit_events','pos.update_releases','pos.digital_receipts']) AS table_name
), table_presence AS (
  SELECT table_name, to_regclass(table_name) IS NOT NULL AS present FROM required_tables
), scope AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.stores WHERE tenant_id=:'tenant_uuid'::uuid AND status='active' AND deleted_at IS NULL) AS active_store_count,
    (SELECT count(*)::bigint FROM pos.terminals WHERE tenant_id=:'tenant_uuid'::uuid AND status IN ('active','pending') AND deleted_at IS NULL) AS available_terminal_count,
    (SELECT count(*)::bigint FROM pos.cash_shifts WHERE tenant_id=:'tenant_uuid'::uuid AND status='open') AS open_shift_count,
    (SELECT count(*)::bigint FROM pos.cash_shifts WHERE tenant_id=:'tenant_uuid'::uuid AND status='closed') AS closed_shift_count,
    (SELECT count(*)::bigint FROM pos.cash_shifts WHERE tenant_id=:'tenant_uuid'::uuid AND status='closed' AND closed_at>=now()-interval '24 hours' AND coalesce(difference_cents,0)<>0) AS cash_difference_last_24h_count,
    (SELECT count(*)::bigint FROM pos.update_releases WHERE tenant_id=:'tenant_uuid'::uuid AND channel='stable' AND revoked_at IS NULL) AS active_stable_release_count,
    :'max_stores_value'::int AS max_stores
), sync_integrity AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id=:'tenant_uuid'::uuid AND status='pending') AS pending_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id=:'tenant_uuid'::uuid AND status='processing') AS processing_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id=:'tenant_uuid'::uuid AND status='retry_pending') AS retry_pending_count,
    (SELECT count(*)::bigint FROM pos.sync_conflicts WHERE tenant_id=:'tenant_uuid'::uuid AND status='pending') AS conflict_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id=:'tenant_uuid'::uuid AND status='dead_letter') AS dead_letter_count,
    :'allowed_conflicts_value'::int AS allowed_existing_sync_conflict_count,
    :'allowed_dead_letters_value'::int AS allowed_dead_letter_count
), negative_stock AS (
  SELECT count(*)::bigint AS negative_stock_count FROM (
    SELECT store_id, product_id, variant_id FROM pos.inventory_ledger WHERE tenant_id=:'tenant_uuid'::uuid GROUP BY store_id, product_id, variant_id HAVING sum(quantity_delta)<0
  ) x
), financial AS (
  SELECT
    (SELECT count(*)::bigint FROM (SELECT tenant_id,terminal_id,local_sale_id FROM pos.sales WHERE tenant_id=:'tenant_uuid'::uuid GROUP BY tenant_id,terminal_id,local_sale_id HAVING count(*)>1) d) AS duplicate_local_sale_count,
    (SELECT count(*)::bigint FROM pos.payments WHERE tenant_id=:'tenant_uuid'::uuid AND amount_cents<0) AS negative_payment_count
), pressure AS (
  SELECT count(*) FILTER (WHERE wait_event IS NOT NULL)::bigint AS waiting_connection_count,
         count(*) FILTER (WHERE state='active' AND now()-query_start>interval '30 seconds')::bigint AS long_running_query_count,
         :'allowed_waiting_connections_value'::int AS allowed_waiting_connection_count
  FROM pg_stat_activity WHERE datname=current_database()
), rls AS (
  SELECT count(*) FILTER (WHERE c.relrowsecurity=true)::bigint AS rls_enabled_table_count,
         count(*) FILTER (WHERE c.relrowsecurity=false)::bigint AS rls_missing_table_count
  FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
  WHERE n.nspname='pos' AND c.relkind='r' AND EXISTS (
    SELECT 1 FROM information_schema.columns col WHERE col.table_schema='pos' AND col.table_name=c.relname AND col.column_name='tenant_id'
  )
), activity AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.sales WHERE tenant_id=:'tenant_uuid'::uuid AND status='completed' AND occurred_at>=now()-interval '24 hours') AS completed_sales_24h,
    (SELECT count(*)::bigint FROM pos.payments WHERE tenant_id=:'tenant_uuid'::uuid AND created_at>=now()-interval '24 hours') AS payments_24h,
    (SELECT count(*)::bigint FROM pos.digital_receipts WHERE tenant_id=:'tenant_uuid'::uuid AND issued_at>=now()-interval '24 hours') AS receipts_issued_24h,
    (SELECT count(*)::bigint FROM pos.audit_events WHERE tenant_id=:'tenant_uuid'::uuid AND occurred_at>=now()-interval '24 hours') AS audit_events_24h
), payload AS (
  SELECT jsonb_build_object(
    'tenantId', :'tenant_uuid',
    'configPresent', EXISTS(SELECT 1 FROM cfg),
    'generalAvailabilityActivated', COALESCE((SELECT (feature_flags->>'generalAvailabilityActivated')::boolean FROM cfg),false),
    'publicGeneralAvailabilityActivated', COALESCE((SELECT (feature_flags->>'publicGeneralAvailabilityActivated')::boolean FROM cfg),false),
    'publicGaActivation', COALESCE((SELECT feature_flags->>'publicGaActivation' FROM cfg),'NOT_ACTIVATED'),
    'rolloutStage', COALESCE((SELECT feature_flags->>'rolloutStage' FROM cfg),'limited_ga'),
    'publicGaActivatedAt',(SELECT feature_flags->>'publicGaActivatedAt' FROM cfg),
    'publicGaActivationSource',(SELECT feature_flags->>'publicGaActivationSource' FROM cfg),
    'tenantConfigVersion',COALESCE((SELECT version FROM cfg),0),
    'requiredTablesPresent',NOT EXISTS(SELECT 1 FROM table_presence WHERE present=false),
    'missingRequiredTables',COALESCE((SELECT jsonb_agg(table_name) FROM table_presence WHERE present=false),'[]'::jsonb),
    'rolloutScope',(SELECT row_to_json(scope) FROM scope),
    'syncIntegrity',(SELECT row_to_json(sync_integrity) FROM sync_integrity),
    'negativeStock',(SELECT row_to_json(negative_stock) FROM negative_stock),
    'financialIntegrity',(SELECT row_to_json(financial) FROM financial),
    'databasePressure',(SELECT row_to_json(pressure) FROM pressure),
    'rls',(SELECT row_to_json(rls) FROM rls),
    'monitoringActivity',(SELECT row_to_json(activity) FROM activity),
    'schemaVersion',4,
    'syncContract','schema_version_4',
    'generatedAt',now()
  ) result
)
SELECT 'POST_PUBLIC_GA_VALIDATION_JSON:'||result::text FROM payload;
