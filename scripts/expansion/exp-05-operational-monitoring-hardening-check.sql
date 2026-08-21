WITH params AS (SELECT :'tenant_id'::uuid AS tenant_id),
tables AS (
  SELECT to_regclass('pos.tenants') IS NOT NULL AS has_tenants,
         to_regclass('pos.stores') IS NOT NULL AS has_stores,
         to_regclass('pos.terminals') IS NOT NULL AS has_terminals,
         to_regclass('pos.users') IS NOT NULL AS has_users,
         to_regclass('pos.sales') IS NOT NULL AS has_sales,
         to_regclass('pos.payments') IS NOT NULL AS has_payments,
         to_regclass('pos.cash_shifts') IS NOT NULL AS has_cash_shifts,
         to_regclass('pos.digital_receipts') IS NOT NULL AS has_digital_receipts,
         to_regclass('pos.inventory_ledger') IS NOT NULL AS has_inventory_ledger,
         to_regclass('pos.sync_inbox_events') IS NOT NULL AS has_sync_inbox_events,
         to_regclass('pos.sync_conflicts') IS NOT NULL AS has_sync_conflicts,
         to_regclass('pos.audit_events') IS NOT NULL AS has_audit_events
), table_status AS (
  SELECT *, (has_tenants AND has_stores AND has_terminals AND has_users AND has_sales AND has_payments AND has_cash_shifts AND has_digital_receipts AND has_inventory_ledger AND has_sync_inbox_events AND has_sync_conflicts AND has_audit_events) AS required_tables_present FROM tables
), inventory_stock AS (
  SELECT l.tenant_id, l.store_id, l.product_id, l.variant_id, l.unit_id, sum(l.quantity_delta) AS quantity_on_hand
  FROM pos.inventory_ledger l
  JOIN params p ON p.tenant_id = l.tenant_id
  GROUP BY l.tenant_id, l.store_id, l.product_id, l.variant_id, l.unit_id
), counts AS (
  SELECT EXISTS (SELECT 1 FROM pos.tenants t JOIN params p ON p.tenant_id = t.id WHERE t.status = 'active' AND t.deleted_at IS NULL) AS tenant_active,
         (SELECT count(*) FROM pos.stores s JOIN params p ON p.tenant_id = s.tenant_id WHERE s.deleted_at IS NULL) AS store_count,
         (SELECT count(*) FROM pos.stores s JOIN params p ON p.tenant_id = s.tenant_id WHERE s.status = 'active' AND s.deleted_at IS NULL) AS active_store_count,
         (SELECT count(*) FROM pos.terminals tm JOIN params p ON p.tenant_id = tm.tenant_id WHERE tm.deleted_at IS NULL) AS terminal_count,
         (SELECT count(*) FROM pos.terminals tm JOIN params p ON p.tenant_id = tm.tenant_id WHERE tm.status = 'active' AND tm.deleted_at IS NULL) AS active_terminal_count,
         (SELECT count(*) FROM pos.users u JOIN params p ON p.tenant_id = u.tenant_id WHERE u.status = 'active' AND u.deleted_at IS NULL) AS active_user_count,
         (SELECT count(*) FROM pos.sales s JOIN params p ON p.tenant_id = s.tenant_id) AS total_sales_count,
         (SELECT count(*) FROM pos.sales s JOIN params p ON p.tenant_id = s.tenant_id WHERE s.status = 'completed' AND s.occurred_at >= now() - interval '24 hours') AS completed_sales_last_24_hours,
         (SELECT count(*) FROM pos.payments pmt JOIN params p ON p.tenant_id = pmt.tenant_id WHERE lower(coalesce(pmt.status,'')) in ('approved','completed')) AS approved_payment_count,
         (SELECT count(*) FROM pos.payments pmt JOIN params p ON p.tenant_id = pmt.tenant_id WHERE lower(coalesce(pmt.status,'')) in ('failed','declined','error') AND pmt.created_at >= now() - interval '24 hours') AS failed_payments_last_24_hours,
         (SELECT count(*) FROM pos.cash_shifts cs JOIN params p ON p.tenant_id = cs.tenant_id WHERE lower(coalesce(cs.status,'')) = 'open') AS open_shift_count,
         (SELECT count(*) FROM pos.cash_shifts cs JOIN params p ON p.tenant_id = cs.tenant_id WHERE lower(coalesce(cs.status,'')) = 'closed' AND coalesce(cs.difference_cents,0) <> 0 AND cs.closed_at >= now() - interval '24 hours') AS cash_difference_last_24_hours_count,
         (SELECT count(*) FROM pos.digital_receipts dr JOIN params p ON p.tenant_id = dr.tenant_id) AS digital_receipt_count,
         (SELECT count(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id = sie.tenant_id AND lower(coalesce(sie.status,'')) = 'processed') AS processed_sync_count,
         (SELECT count(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id = sie.tenant_id AND lower(coalesce(sie.status,'')) = 'retry_pending') AS retry_pending_sync_count,
         (SELECT count(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id = sie.tenant_id AND lower(coalesce(sie.status,'')) = 'dead_letter') AS dead_letter_sync_count,
         (SELECT count(*) FROM pos.sync_conflicts sc JOIN params p ON p.tenant_id = sc.tenant_id AND lower(coalesce(sc.status,'')) = 'pending') AS pending_conflict_count,
         (SELECT count(*) FROM pos.sync_conflicts sc JOIN params p ON p.tenant_id = sc.tenant_id AND lower(coalesce(sc.status,'')) = 'resolved') AS resolved_conflict_count,
         (SELECT count(*) FROM inventory_stock stock WHERE stock.tenant_id = p.tenant_id AND stock.quantity_on_hand < 0) AS negative_inventory_item_count,
         (SELECT count(*) FROM pos.audit_events ae JOIN params p ON p.tenant_id = ae.tenant_id) AS audit_event_count,
         (SELECT count(*) FROM pos.audit_events ae JOIN params p ON p.tenant_id = ae.tenant_id WHERE ae.occurred_at >= now() - interval '24 hours') AS audit_events_last_24_hours,
         4 AS schema_version
  FROM params p
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT ts.required_tables_present THEN 'required_table_missing' END,
    CASE WHEN NOT c.tenant_active THEN 'tenant_missing_or_inactive' END,
    CASE WHEN c.active_store_count < 2 THEN 'second_store_not_available_for_monitoring' END,
    CASE WHEN c.active_terminal_count < 2 THEN 'second_terminal_not_available_for_monitoring' END,
    CASE WHEN c.active_user_count < 1 THEN 'active_user_missing' END,
    CASE WHEN c.total_sales_count < 1 THEN 'sales_evidence_missing' END,
    CASE WHEN c.approved_payment_count < 1 THEN 'approved_payment_evidence_missing' END,
    CASE WHEN c.pending_conflict_count > 0 THEN 'pending_conflicts_block_operational_monitoring' END,
    CASE WHEN c.failed_payments_last_24_hours > 0 THEN 'failed_payments_last_24h_block_operational_monitoring' END,
    CASE WHEN c.cash_difference_last_24_hours_count > 0 THEN 'cash_shift_difference_last_24h_block_operational_monitoring' END
  ], NULL) AS sql_blocking_reasons,
  array_remove(ARRAY[
    CASE WHEN c.retry_pending_sync_count > 0 THEN 'retry_pending_sync_requires_monitoring' END,
    CASE WHEN c.dead_letter_sync_count > 0 THEN 'dead_letter_sync_requires_triage' END,
    CASE WHEN c.negative_inventory_item_count > 0 THEN 'negative_inventory_requires_reconciliation' END,
    CASE WHEN c.open_shift_count > 0 THEN 'open_cash_shift_requires_daily_review' END
  ], NULL) AS sql_warnings
  FROM table_status ts CROSS JOIN counts c
)
SELECT json_build_object(
  'exp05SqlValidation', CASE WHEN array_length(b.sql_blocking_reasons,1) IS NULL THEN 'GO' ELSE 'NO-GO' END,
  'sqlBlockingReasons', coalesce(b.sql_blocking_reasons, ARRAY[]::text[]),
  'sqlWarnings', coalesce(b.sql_warnings, ARRAY[]::text[]),
  'requiredTablesPresent', ts.required_tables_present,
  'tenantActive', c.tenant_active,
  'storeCount', c.store_count,
  'activeStoreCount', c.active_store_count,
  'terminalCount', c.terminal_count,
  'activeTerminalCount', c.active_terminal_count,
  'activeUserCount', c.active_user_count,
  'totalSalesCount', c.total_sales_count,
  'completedSalesLast24Hours', c.completed_sales_last_24_hours,
  'approvedPaymentCount', c.approved_payment_count,
  'failedPaymentsLast24Hours', c.failed_payments_last_24_hours,
  'openShiftCount', c.open_shift_count,
  'cashDifferenceLast24HoursCount', c.cash_difference_last_24_hours_count,
  'digitalReceiptCount', c.digital_receipt_count,
  'processedSyncCount', c.processed_sync_count,
  'retryPendingSyncCount', c.retry_pending_sync_count,
  'deadLetterSyncCount', c.dead_letter_sync_count,
  'pendingConflictCount', c.pending_conflict_count,
  'resolvedConflictCount', c.resolved_conflict_count,
  'negativeInventoryItemCount', c.negative_inventory_item_count,
  'auditEventCount', c.audit_event_count,
  'auditEventsLast24Hours', c.audit_events_last_24_hours,
  'schemaVersion', c.schema_version,
  'monitoringContract', 'operational_monitoring_hardening'
)::text
FROM table_status ts CROSS JOIN counts c CROSS JOIN blockers b;
