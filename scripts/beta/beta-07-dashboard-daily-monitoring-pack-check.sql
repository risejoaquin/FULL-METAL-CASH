\set ON_ERROR_STOP on

WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         now() AS checked_at,
         interval '24 hours' AS window_24h,
         interval '15 minutes' AS retry_sla,
         interval '15 minutes' AS processing_sla
), table_status AS (
  SELECT bool_and(to_regclass(table_name) IS NOT NULL) AS required_tables_present
  FROM (VALUES
    ('pos.tenants'),('pos.stores'),('pos.terminals'),('pos.sales'),('pos.payments'),
    ('pos.cash_shifts'),('pos.inventory_ledger'),('pos.inventory_low_stock_thresholds'),
    ('pos.sync_inbox_events'),('pos.sync_conflicts'),('pos.audit_events')
  ) required(table_name)
), inventory_stock AS (
  SELECT l.store_id, l.product_id, l.variant_id, sum(l.quantity_delta) AS quantity_on_hand
  FROM pos.inventory_ledger l JOIN params p ON p.tenant_id=l.tenant_id
  GROUP BY l.store_id,l.product_id,l.variant_id
), topology AS (
  SELECT
    EXISTS(SELECT 1 FROM pos.tenants t JOIN params p ON p.tenant_id=t.id WHERE t.status='active' AND t.deleted_at IS NULL) AS tenant_active,
    (SELECT count(*) FROM pos.stores s JOIN params p ON p.tenant_id=s.tenant_id WHERE s.status='active' AND s.deleted_at IS NULL) AS active_store_count,
    (SELECT count(*) FROM pos.terminals t JOIN params p ON p.tenant_id=t.tenant_id WHERE t.status='active' AND t.deleted_at IS NULL) AS active_terminal_count
), sync_state AS (
  SELECT
    count(*) FILTER (WHERE lower(coalesce(i.status,''))='processed') AS processed_count,
    count(*) FILTER (WHERE lower(coalesce(i.status,''))='retry_pending') AS retry_pending_count,
    count(*) FILTER (WHERE lower(coalesce(i.status,''))='retry_pending' AND coalesce(i.next_retry_at,i.created_at) <= p.checked_at) AS retry_due_count,
    count(*) FILTER (WHERE lower(coalesce(i.status,''))='retry_pending' AND i.created_at < p.checked_at-p.retry_sla) AS retry_over_sla_count,
    count(*) FILTER (WHERE lower(coalesce(i.status,''))='dead_letter') AS dead_letter_count,
    count(*) FILTER (WHERE lower(coalesce(i.status,''))='processing' AND coalesce(i.last_attempt_at,i.created_at) < p.checked_at-p.processing_sla) AS stale_processing_count,
    count(*) FILTER (WHERE coalesce(i.schema_version,4) <> 4) AS legacy_schema_event_count
  FROM params p LEFT JOIN pos.sync_inbox_events i ON p.tenant_id=i.tenant_id GROUP BY p.checked_at,p.retry_sla,p.processing_sla
), conflicts AS (
  SELECT
    count(*) FILTER (WHERE lower(coalesce(c.status,''))='pending') AS pending_count,
    count(*) FILTER (WHERE lower(coalesce(c.status,''))='resolved') AS resolved_count
  FROM pos.sync_conflicts c JOIN params p ON p.tenant_id=c.tenant_id
), daily_ops AS (
  SELECT
    (SELECT count(*) FROM pos.sales s JOIN params p ON p.tenant_id=s.tenant_id WHERE s.status='completed' AND s.occurred_at>=p.checked_at-p.window_24h) AS completed_sales_24h,
    (SELECT count(*) FROM pos.payments py JOIN params p ON p.tenant_id=py.tenant_id WHERE lower(coalesce(py.status,'')) IN ('failed','declined','error') AND py.created_at>=p.checked_at-p.window_24h) AS failed_payments_24h,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN params p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='open') AS open_shift_count,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN params p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='closed' AND coalesce(cs.difference_cents,0)<>0 AND cs.closed_at>=p.checked_at-p.window_24h) AS cash_difference_24h,
    (SELECT count(*) FROM pos.audit_events a JOIN params p ON p.tenant_id=a.tenant_id WHERE a.occurred_at>=p.checked_at-p.window_24h) AS audit_events_24h,
    (SELECT count(*) FROM inventory_stock st WHERE st.quantity_on_hand<0) AS negative_inventory_count,
    (SELECT count(*) FROM inventory_stock st JOIN pos.inventory_low_stock_thresholds th ON th.store_id=st.store_id AND th.product_id=st.product_id AND th.variant_id IS NOT DISTINCT FROM st.variant_id JOIN params p ON p.tenant_id=th.tenant_id WHERE st.quantity_on_hand>=0 AND st.quantity_on_hand<=th.reorder_point) AS low_stock_count
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT ts.required_tables_present THEN 'required_table_missing' END,
    CASE WHEN NOT topo.tenant_active THEN 'tenant_missing_or_inactive' END,
    CASE WHEN topo.active_store_count<1 THEN 'active_store_missing' END,
    CASE WHEN topo.active_terminal_count<1 THEN 'active_terminal_missing' END,
    CASE WHEN ss.stale_processing_count>0 THEN 'stale_sync_processing_requires_incident' END,
    CASE WHEN cf.pending_count>0 THEN 'pending_conflict_requires_incident' END,
    CASE WHEN ops.failed_payments_24h>0 THEN 'failed_payment_last_24h_requires_incident' END,
    CASE WHEN ops.cash_difference_24h>0 THEN 'cash_difference_last_24h_requires_incident' END,
    CASE WHEN ops.audit_events_24h<1 THEN 'audit_evidence_missing' END,
    CASE WHEN ss.legacy_schema_event_count>0 THEN 'legacy_sync_schema_event_detected' END
  ],NULL) AS blocker_list,
  array_remove(ARRAY[
    CASE WHEN ss.retry_pending_count>0 THEN 'retry_pending_sync_requires_triage' END,
    CASE WHEN ss.retry_due_count>0 THEN 'retry_due_requires_worker_or_manual_retry' END,
    CASE WHEN ss.retry_over_sla_count>0 THEN 'retry_over_sla_requires_support_ticket' END,
    CASE WHEN ss.dead_letter_count>0 THEN 'dead_letter_sync_requires_triage' END,
    CASE WHEN ops.open_shift_count>0 THEN 'open_cash_shift_requires_daily_review' END,
    CASE WHEN ops.negative_inventory_count>0 THEN 'negative_inventory_requires_reconciliation' END,
    CASE WHEN ops.low_stock_count>0 THEN 'low_stock_requires_replenishment_review' END
  ],NULL) AS condition_list
  FROM table_status ts CROSS JOIN topology topo CROSS JOIN sync_state ss CROSS JOIN conflicts cf CROSS JOIN daily_ops ops
)
SELECT json_build_object(
  'beta07SqlDecision',CASE WHEN array_length(b.blocker_list,1) IS NULL THEN 'GO' ELSE 'NO-GO' END,
  'blockers',coalesce(b.blocker_list,ARRAY[]::text[]),
  'conditions',coalesce(b.condition_list,ARRAY[]::text[]),
  'requiredTablesPresent',ts.required_tables_present,
  'tenantActive',topo.tenant_active,
  'activeStoreCount',topo.active_store_count,
  'activeTerminalCount',topo.active_terminal_count,
  'processedSyncCount',ss.processed_count,
  'retryPendingSync',ss.retry_pending_count,
  'retryDueCount',ss.retry_due_count,
  'retryOverSlaCount',ss.retry_over_sla_count,
  'deadLetterSync',ss.dead_letter_count,
  'staleProcessingCount',ss.stale_processing_count,
  'pendingConflictCount',cf.pending_count,
  'resolvedConflictCount',cf.resolved_count,
  'completedSalesLast24Hours',ops.completed_sales_24h,
  'failedPaymentsLast24Hours',ops.failed_payments_24h,
  'openShiftCount',ops.open_shift_count,
  'cashDifferenceLast24HoursCount',ops.cash_difference_24h,
  'negativeInventoryItemCount',ops.negative_inventory_count,
  'lowStockItemCount',ops.low_stock_count,
  'auditEventsLast24Hours',ops.audit_events_24h,
  'legacySchemaEventCount',ss.legacy_schema_event_count,
  'schemaVersion',4,
  'syncContract','schema_version_4',
  'monitoringContract','beta_dashboard_daily_monitoring_pack'
)::text
FROM table_status ts CROSS JOIN topology topo CROSS JOIN sync_state ss CROSS JOIN conflicts cf CROSS JOIN daily_ops ops CROSS JOIN blockers b;
