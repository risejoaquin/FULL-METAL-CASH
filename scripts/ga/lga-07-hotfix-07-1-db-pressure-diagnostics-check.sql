\set tenant_uuid :tenant_id
WITH activity AS (
  SELECT
    pid,
    usename,
    application_name,
    client_addr::text AS client_addr,
    state,
    wait_event_type,
    wait_event,
    now() - COALESCE(query_start, backend_start) AS age,
    now() - backend_start AS backend_age,
    left(regexp_replace(COALESCE(query, ''), '\s+', ' ', 'g'), 240) AS query_sample
  FROM pg_stat_activity
  WHERE datname = current_database()
), grouped AS (
  SELECT
    COALESCE(NULLIF(application_name,''), '(none)') AS application_name,
    COALESCE(state, '(none)') AS state,
    COALESCE(wait_event_type, '(none)') AS wait_event_type,
    COALESCE(wait_event, '(none)') AS wait_event,
    count(*)::bigint AS count
  FROM activity
  GROUP BY 1,2,3,4
  ORDER BY count DESC, application_name, state
), waits AS (
  SELECT
    count(*) FILTER (WHERE wait_event IS NOT NULL)::bigint AS waiting_connection_count,
    count(*) FILTER (WHERE state = 'active')::bigint AS active_connection_count,
    count(*) FILTER (WHERE state = 'idle')::bigint AS idle_connection_count,
    count(*) FILTER (WHERE state = 'idle in transaction')::bigint AS idle_in_transaction_connection_count,
    count(*) FILTER (WHERE state = 'active' AND now() - query_start > interval '30 seconds')::bigint AS long_running_query_count,
    count(*)::bigint AS observed_connection_count
  FROM activity
), samples AS (
  SELECT jsonb_agg(row_to_json(s) ORDER BY s.age DESC) AS items
  FROM (
    SELECT pid, application_name, state, wait_event_type, wait_event, age::text AS age, backend_age::text AS backend_age, query_sample
    FROM activity
    WHERE wait_event IS NOT NULL OR state IN ('active','idle in transaction')
    ORDER BY age DESC
    LIMIT 30
  ) s
), tenant_baseline AS (
  SELECT jsonb_build_object(
    'tenantId', :'tenant_uuid',
    'completedSales24h', (SELECT count(*)::bigint FROM pos.sales WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'completed' AND occurred_at >= now() - interval '24 hours'),
    'payments24h', (SELECT count(*)::bigint FROM pos.payments WHERE tenant_id = :'tenant_uuid'::uuid AND created_at >= now() - interval '24 hours'),
    'receiptsIssued24h', (SELECT count(*)::bigint FROM pos.digital_receipts WHERE tenant_id = :'tenant_uuid'::uuid AND issued_at >= now() - interval '24 hours'),
    'syncPending', (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'pending'),
    'syncProcessing', (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'processing'),
    'syncRetryPending', (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'retry_pending'),
    'syncConflicts', (SELECT count(*)::bigint FROM pos.sync_conflicts WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'pending'),
    'syncDeadLetters', (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'dead_letter')
  ) AS result
), payload AS (
  SELECT jsonb_build_object(
    'schemaVersion', 4,
    'syncContract', 'schema_version_4',
    'generatedAt', now(),
    'databasePressure', (SELECT row_to_json(waits) FROM waits),
    'activityGroups', COALESCE((SELECT jsonb_agg(row_to_json(grouped)) FROM grouped), '[]'::jsonb),
    'activitySamples', COALESCE((SELECT items FROM samples), '[]'::jsonb),
    'tenantBaseline', (SELECT result FROM tenant_baseline),
    'publicGaActivation', 'NOT_ACTIVATED',
    'diagnosticOnly', true
  ) AS result
)
SELECT 'LGA071_DIAGNOSTIC_JSON:' || result::text FROM payload;
