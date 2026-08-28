\set tenant_uuid :tenant_id
WITH negative_stock AS (
  SELECT store_id, product_id, variant_id, sum(quantity_delta) AS quantity_on_hand
  FROM pos.inventory_ledger
  WHERE tenant_id = :'tenant_uuid'::uuid
  GROUP BY store_id, product_id, variant_id
  HAVING sum(quantity_delta) < 0
), negative_stock_detail AS (
  SELECT ns.store_id,
         ns.product_id,
         ns.variant_id,
         p.sku,
         p.name,
         u.id AS unit_id,
         u.code AS unit_code,
         ns.quantity_on_hand,
         abs(ns.quantity_on_hand) AS adjustment_quantity_needed
  FROM negative_stock ns
  JOIN pos.products p ON p.id = ns.product_id
  JOIN pos.units u ON u.id = (
    SELECT il.unit_id
    FROM pos.inventory_ledger il
    WHERE il.tenant_id = :'tenant_uuid'::uuid
      AND il.store_id = ns.store_id
      AND il.product_id = ns.product_id
      AND (il.variant_id IS NOT DISTINCT FROM ns.variant_id)
    ORDER BY il.created_at DESC
    LIMIT 1
  )
  ORDER BY ns.quantity_on_hand ASC
), monitoring_activity AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.sales WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'completed' AND occurred_at >= now() - interval '24 hours') AS completed_sales_24h,
    (SELECT count(*)::bigint FROM pos.payments WHERE tenant_id = :'tenant_uuid'::uuid AND created_at >= now() - interval '24 hours') AS payments_24h,
    (SELECT count(*)::bigint FROM pos.digital_receipts WHERE tenant_id = :'tenant_uuid'::uuid AND issued_at >= now() - interval '24 hours') AS receipts_issued_24h,
    (SELECT count(*)::bigint FROM pos.audit_events WHERE tenant_id = :'tenant_uuid'::uuid AND occurred_at >= now() - interval '24 hours') AS audit_events_24h
), sync_integrity AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'pending') AS sync_pending_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'processing') AS sync_processing_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'retry_pending') AS sync_retry_pending_count,
    (SELECT count(*)::bigint FROM pos.sync_conflicts WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'pending') AS sync_conflict_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id = :'tenant_uuid'::uuid AND status = 'dead_letter') AS sync_dead_letter_count
), db_pressure AS (
  SELECT count(*) FILTER (WHERE wait_event IS NOT NULL)::bigint AS waiting_connection_count,
         count(*) FILTER (WHERE state = 'active')::bigint AS active_connection_count,
         count(*) FILTER (WHERE state = 'idle')::bigint AS idle_connection_count,
         count(*) FILTER (WHERE state = 'idle in transaction')::bigint AS idle_in_transaction_connection_count,
         count(*)::bigint AS observed_connection_count
  FROM pg_stat_activity WHERE datname = current_database()
), payload AS (
  SELECT jsonb_build_object(
    'schemaVersion', 4,
    'syncContract', 'schema_version_4',
    'tenantId', :'tenant_uuid',
    'generatedAt', now(),
    'negativeStock', jsonb_build_object('count', (SELECT count(*) FROM negative_stock_detail), 'items', COALESCE((SELECT jsonb_agg(row_to_json(negative_stock_detail)) FROM negative_stock_detail), '[]'::jsonb)),
    'monitoringActivity', (SELECT row_to_json(monitoring_activity) FROM monitoring_activity),
    'syncIntegrity', (SELECT row_to_json(sync_integrity) FROM sync_integrity),
    'databasePressure', (SELECT row_to_json(db_pressure) FROM db_pressure),
    'publicGaActivation', 'NOT_ACTIVATED',
    'diagnosticOnly', true
  ) AS result
)
SELECT 'LGA073_INVENTORY_JSON:' || result::text FROM payload;
