\set tenant_uuid :tenant_id
WITH cfg AS (
  SELECT tenant_id, feature_flags, version, updated_at
  FROM pos.tenant_configs
  WHERE tenant_id = :'tenant_uuid'::uuid
), pressure AS (
  SELECT
    count(*) FILTER (WHERE wait_event IS NOT NULL)::bigint AS waiting_connection_count,
    count(*) FILTER (WHERE state = 'active' AND now() - query_start > interval '30 seconds')::bigint AS long_running_query_count
  FROM pg_stat_activity
  WHERE datname = current_database()
), negative_stock AS (
  SELECT count(*)::bigint AS negative_stock_count
  FROM (
    SELECT store_id, product_id, variant_id
    FROM pos.inventory_ledger
    WHERE tenant_id = :'tenant_uuid'::uuid
    GROUP BY store_id, product_id, variant_id
    HAVING sum(quantity_delta) < 0
  ) x
), sync_state AS (
  SELECT
    count(*) FILTER (WHERE status = 'pending')::bigint AS pending_count,
    count(*) FILTER (WHERE status = 'processing')::bigint AS processing_count,
    count(*) FILTER (WHERE status = 'retry_pending')::bigint AS retry_pending_count,
    count(*) FILTER (WHERE status = 'dead_letter')::bigint AS dead_letter_count
  FROM pos.sync_inbox_events
  WHERE tenant_id = :'tenant_uuid'::uuid
), payload AS (
  SELECT jsonb_build_object(
    'tenantId', :'tenant_uuid',
    'configPresent', EXISTS (SELECT 1 FROM cfg),
    'generalAvailabilityActivated', COALESCE((SELECT (feature_flags->>'generalAvailabilityActivated')::boolean FROM cfg), false),
    'publicGeneralAvailabilityActivated', COALESCE((SELECT (feature_flags->>'publicGeneralAvailabilityActivated')::boolean FROM cfg), false),
    'publicGaActivation', COALESCE((SELECT feature_flags->>'publicGaActivation' FROM cfg), 'NOT_ACTIVATED'),
    'rolloutStage', COALESCE((SELECT feature_flags->>'rolloutStage' FROM cfg), 'limited_ga'),
    'publicGaActivatedAt', (SELECT feature_flags->>'publicGaActivatedAt' FROM cfg),
    'publicGaActivationSource', (SELECT feature_flags->>'publicGaActivationSource' FROM cfg),
    'tenantConfigVersion', COALESCE((SELECT version FROM cfg), 0),
    'tenantConfigUpdatedAt', (SELECT updated_at FROM cfg),
    'waitingConnectionCount', (SELECT waiting_connection_count FROM pressure),
    'longRunningQueryCount', (SELECT long_running_query_count FROM pressure),
    'negativeStockCount', (SELECT negative_stock_count FROM negative_stock),
    'sync', (SELECT row_to_json(sync_state) FROM sync_state),
    'schemaVersion', 4,
    'syncContract', 'schema_version_4',
    'generatedAt', now()
  ) AS result
)
SELECT 'PUBLIC_GA_ACTIVATION_STATE_JSON:' || result::text FROM payload;
