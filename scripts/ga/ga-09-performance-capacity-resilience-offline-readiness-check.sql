\set ON_ERROR_STOP on
SET search_path TO pos, public;

WITH tenant_tables AS (
  SELECT c.oid, n.nspname, c.relname
  FROM pg_class c
  JOIN pg_namespace n ON n.oid = c.relnamespace
  JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'tenant_id' AND NOT a.attisdropped
  WHERE n.nspname = 'pos'
    AND c.relkind = 'r'
),
rls AS (
  SELECT
    count(*)::bigint AS "tenantTableCount",
    count(*) FILTER (WHERE c.relrowsecurity)::bigint AS "rlsEnabledTenantTableCount",
    count(*) FILTER (WHERE NOT c.relrowsecurity)::bigint AS "rlsMissingTenantTableCount",
    count(*) FILTER (WHERE NOT EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid))::bigint AS "rlsPolicyMissingTenantTableCount"
  FROM tenant_tables t
  JOIN pg_class c ON c.oid = t.oid
),
sync_status AS (
  SELECT COALESCE(jsonb_object_agg(status, status_count), '{}'::jsonb) AS by_status
  FROM (
    SELECT status, count(*)::bigint AS status_count
    FROM sync_inbox_events
    WHERE tenant_id = :'tenant_id'::uuid
    GROUP BY status
  ) s
),
sync_metrics AS (
  SELECT
    count(*) FILTER (WHERE status = 'received')::bigint AS "receivedCount",
    count(*) FILTER (WHERE status = 'processing')::bigint AS "processingCount",
    count(*) FILTER (WHERE status = 'processed')::bigint AS "processedCount",
    count(*) FILTER (WHERE status = 'duplicate')::bigint AS "duplicateCount",
    count(*) FILTER (WHERE status = 'rejected')::bigint AS "rejectedCount",
    count(*) FILTER (WHERE status = 'retry_pending')::bigint AS "retryPendingCount",
    count(*) FILTER (WHERE status = 'retry_pending' AND created_at < now() - interval '72 hours')::bigint AS "retryOverSlaCount",
    count(*) FILTER (WHERE status = 'conflict')::bigint AS "conflictCount",
    count(*) FILTER (WHERE status = 'dead_letter')::bigint AS "deadLetterCount",
    count(*) FILTER (WHERE schema_version <> 4)::bigint AS "legacySchemaEventCount",
    min(CASE WHEN status IN ('received','retry_pending') THEN created_at ELSE NULL END) AS "oldestPendingAt",
    max(processed_at) AS "lastProcessedAt"
  FROM sync_inbox_events
  WHERE tenant_id = :'tenant_id'::uuid
),
conflict_metrics AS (
  SELECT
    count(*) FILTER (WHERE status = 'pending')::bigint AS "pendingConflictCount",
    count(*) FILTER (WHERE status = 'resolved')::bigint AS "resolvedConflictCount"
  FROM sync_conflicts
  WHERE tenant_id = :'tenant_id'::uuid
),
financial AS (
  SELECT
    (SELECT count(*)::bigint FROM sales WHERE tenant_id = :'tenant_id'::uuid) AS "salesTotalCount",
    (SELECT count(*)::bigint FROM sales WHERE tenant_id = :'tenant_id'::uuid AND created_at >= now() - interval '24 hours') AS "salesLast24Count",
    (SELECT COALESCE(sum(total_cents),0)::bigint FROM sales WHERE tenant_id = :'tenant_id'::uuid AND status IN ('completed','partially_returned','returned')) AS "salesTotalCents",
    (SELECT count(*)::bigint FROM payments WHERE tenant_id = :'tenant_id'::uuid) AS "paymentTotalCount",
    (SELECT count(*)::bigint FROM payments WHERE tenant_id = :'tenant_id'::uuid AND created_at >= now() - interval '24 hours') AS "paymentLast24Count",
    (SELECT COALESCE(sum(amount_cents),0)::bigint FROM payments WHERE tenant_id = :'tenant_id'::uuid AND status = 'approved') AS "approvedPaymentTotalCents",
    (SELECT count(*)::bigint FROM (
       SELECT tenant_id, terminal_id, local_sale_id
       FROM sales
       WHERE tenant_id = :'tenant_id'::uuid
       GROUP BY tenant_id, terminal_id, local_sale_id
       HAVING count(*) > 1
     ) d) AS "duplicateLocalSaleCount",
    (SELECT count(*)::bigint FROM (
       SELECT tenant_id, sale_id, local_payment_id
       FROM payments
       WHERE tenant_id = :'tenant_id'::uuid
       GROUP BY tenant_id, sale_id, local_payment_id
       HAVING count(*) > 1
     ) d) AS "duplicateLocalPaymentCount"
),
database_pressure AS (
  SELECT
    (SELECT count(*)::bigint FROM pg_stat_activity WHERE datname = current_database()) AS "activeDatabaseConnectionCount",
    (SELECT count(*)::bigint FROM pg_locks WHERE NOT granted) AS "waitingLockCount",
    (SELECT count(*)::bigint FROM pg_stat_activity WHERE datname = current_database() AND state = 'active' AND now() - query_start > interval '30 seconds') AS "longRunningQueryCount"
),
indexes AS (
  SELECT
    to_regclass('pos.idx_sales_tenant_store_date') IS NOT NULL AS "salesTenantStoreDateIndexPresent",
    to_regclass('pos.idx_payments_sale') IS NOT NULL AS "paymentsSaleIndexPresent",
    to_regclass('pos.idx_sync_inbox_tenant_status_created') IS NOT NULL AS "syncInboxTenantStatusCreatedIndexPresent",
    to_regclass('pos.idx_sync_changes_tenant_cursor') IS NOT NULL AS "syncChangesTenantCursorIndexPresent",
    to_regclass('pos.idx_sync_conflicts_tenant_status') IS NOT NULL AS "syncConflictsTenantStatusIndexPresent"
)
SELECT jsonb_build_object(
  'tenantId', :'tenant_id',
  'schemaVersion', 4,
  'syncContract', 'schema_version_4',
  'generatedAt', now(),
  'rls', to_jsonb(rls),
  'syncStatusByBucket', (SELECT by_status FROM sync_status),
  'sync', to_jsonb(sync_metrics),
  'conflicts', to_jsonb(conflict_metrics),
  'financial', to_jsonb(financial),
  'databasePressure', to_jsonb(database_pressure),
  'indexes', to_jsonb(indexes)
)::text
FROM rls, sync_metrics, conflict_metrics, financial, database_pressure, indexes;
