\set ON_ERROR_STOP on

WITH scoped AS (
  SELECT :'tenant_id'::uuid AS tenant_id
), required_tables AS (
  SELECT
    to_regclass('pos.tenants') IS NOT NULL AS has_tenants,
    to_regclass('pos.stores') IS NOT NULL AS has_stores,
    to_regclass('pos.terminals') IS NOT NULL AS has_terminals,
    to_regclass('pos.users') IS NOT NULL AS has_users,
    to_regclass('pos.sales') IS NOT NULL AS has_sales,
    to_regclass('pos.payments') IS NOT NULL AS has_payments,
    to_regclass('pos.cash_shifts') IS NOT NULL AS has_cash_shifts,
    to_regclass('pos.digital_receipts') IS NOT NULL AS has_digital_receipts,
    to_regclass('pos.returns') IS NOT NULL AS has_returns,
    to_regclass('pos.return_refunds') IS NOT NULL AS has_return_refunds,
    to_regclass('pos.inventory_ledger') IS NOT NULL AS has_inventory_ledger,
    to_regclass('pos.sync_inbox_events') IS NOT NULL AS has_sync_inbox_events,
    to_regclass('pos.sync_conflicts') IS NOT NULL AS has_sync_conflicts,
    to_regclass('pos.audit_events') IS NOT NULL AS has_audit_events
), sync_counts AS (
  SELECT e.status, count(*)::bigint AS count
  FROM pos.sync_inbox_events e
  JOIN scoped s ON s.tenant_id = e.tenant_id
  GROUP BY e.status
), inventory_stock AS (
  SELECT l.store_id, l.product_id, l.variant_id, sum(l.quantity_delta) AS quantity_on_hand
  FROM pos.inventory_ledger l
  JOIN scoped s ON s.tenant_id = l.tenant_id
  GROUP BY l.store_id, l.product_id, l.variant_id
), checks AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.tenants t, scoped s WHERE t.id = s.tenant_id) AS tenant_exists_count,
    (SELECT count(*)::bigint FROM pos.stores st, scoped s WHERE st.tenant_id = s.tenant_id) AS store_count,
    (SELECT count(*)::bigint FROM pos.terminals tm, scoped s WHERE tm.tenant_id = s.tenant_id) AS terminal_count,
    (SELECT count(*)::bigint FROM pos.users u, scoped s WHERE u.tenant_id = s.tenant_id) AS user_count,
    (SELECT count(*)::bigint FROM pos.sales sa, scoped s WHERE sa.tenant_id = s.tenant_id) AS total_sales_count,
    (SELECT count(*)::bigint FROM pos.sales sa, scoped s WHERE sa.tenant_id = s.tenant_id AND sa.created_at >= now() - interval '24 hours') AS sales_last_24h,
    (SELECT COALESCE(sum(total_cents),0)::bigint FROM pos.sales sa, scoped s WHERE sa.tenant_id = s.tenant_id AND sa.status IN ('completed','returned')) AS gross_sales_cents,
    (SELECT count(*)::bigint FROM pos.payments p, scoped s WHERE p.tenant_id = s.tenant_id AND p.status = 'approved') AS approved_payment_count,
    (SELECT count(*)::bigint FROM pos.payments p, scoped s WHERE p.tenant_id = s.tenant_id AND p.status IN ('declined', 'voided') AND p.created_at >= now() - interval '24 hours') AS failed_payments_last_24h,
    (SELECT count(*)::bigint FROM pos.cash_shifts cs, scoped s WHERE cs.tenant_id = s.tenant_id AND cs.status = 'closed') AS closed_shift_count,
    (SELECT count(*)::bigint FROM pos.digital_receipts dr, scoped s WHERE dr.tenant_id = s.tenant_id) AS digital_receipt_count,
    (SELECT count(*)::bigint FROM pos.returns r, scoped s WHERE r.tenant_id = s.tenant_id AND r.status = 'completed') AS completed_return_count,
    COALESCE((SELECT sum(count) FROM sync_counts WHERE status = 'processed'), 0)::bigint AS processed_sync_count,
    COALESCE((SELECT sum(count) FROM sync_counts WHERE status = 'retry_pending'), 0)::bigint AS retry_pending_sync_count,
    COALESCE((SELECT sum(count) FROM sync_counts WHERE status = 'dead_letter'), 0)::bigint AS dead_letter_sync_count,
    (SELECT count(*)::bigint FROM pos.sync_conflicts c, scoped s WHERE c.tenant_id = s.tenant_id AND c.status = 'pending') AS pending_conflict_count,
    (SELECT count(*)::bigint FROM pos.sync_conflicts c, scoped s WHERE c.tenant_id = s.tenant_id AND c.status = 'resolved') AS resolved_conflict_count,
    (SELECT count(*)::bigint FROM inventory_stock WHERE quantity_on_hand < 0) AS negative_inventory_item_count,
    (SELECT count(*)::bigint FROM pos.audit_events a, scoped s WHERE a.tenant_id = s.tenant_id) AS audit_event_count,
    (SELECT count(*)::bigint FROM pos.audit_events a, scoped s WHERE a.tenant_id = s.tenant_id AND a.occurred_at >= now() - interval '24 hours') AS audit_events_last_24h
)
SELECT json_build_object(
  'tenantExists', tenant_exists_count = 1,
  'storeCount', store_count,
  'terminalCount', terminal_count,
  'userCount', user_count,
  'totalSalesCount', total_sales_count,
  'salesLast24Hours', sales_last_24h,
  'grossSalesCents', gross_sales_cents,
  'approvedPaymentCount', approved_payment_count,
  'failedPaymentsLast24Hours', failed_payments_last_24h,
  'closedShiftCount', closed_shift_count,
  'digitalReceiptCount', digital_receipt_count,
  'completedReturnCount', completed_return_count,
  'processedSyncCount', processed_sync_count,
  'retryPendingSyncCount', retry_pending_sync_count,
  'deadLetterSyncCount', dead_letter_sync_count,
  'pendingConflictCount', pending_conflict_count,
  'resolvedConflictCount', resolved_conflict_count,
  'negativeInventoryItemCount', negative_inventory_item_count,
  'auditEventCount', audit_event_count,
  'auditEventsLast24Hours', audit_events_last_24h,
  'requiredTablesPresent', (has_tenants AND has_stores AND has_terminals AND has_users AND has_sales AND has_payments AND has_cash_shifts AND has_digital_receipts AND has_returns AND has_return_refunds AND has_inventory_ledger AND has_sync_inbox_events AND has_sync_conflicts AND has_audit_events),
  'sqlBlockingReasons', ARRAY_REMOVE(ARRAY[
    CASE WHEN tenant_exists_count <> 1 THEN 'tenant_missing_or_duplicated' END,
    CASE WHEN store_count <= 0 THEN 'store_count_zero' END,
    CASE WHEN terminal_count <= 0 THEN 'terminal_count_zero' END,
    CASE WHEN NOT (has_tenants AND has_stores AND has_terminals AND has_users AND has_sales AND has_payments AND has_cash_shifts AND has_digital_receipts AND has_returns AND has_return_refunds AND has_inventory_ledger AND has_sync_inbox_events AND has_sync_conflicts AND has_audit_events) THEN 'required_table_missing' END,
    CASE WHEN NOT has_tenants THEN 'missing_pos_tenants' END,
    CASE WHEN NOT has_stores THEN 'missing_pos_stores' END,
    CASE WHEN NOT has_terminals THEN 'missing_pos_terminals' END,
    CASE WHEN NOT has_users THEN 'missing_pos_users' END,
    CASE WHEN NOT has_sales THEN 'missing_pos_sales' END,
    CASE WHEN NOT has_payments THEN 'missing_pos_payments' END,
    CASE WHEN NOT has_cash_shifts THEN 'missing_pos_cash_shifts' END,
    CASE WHEN NOT has_digital_receipts THEN 'missing_pos_digital_receipts' END,
    CASE WHEN NOT has_returns THEN 'missing_pos_returns' END,
    CASE WHEN NOT has_return_refunds THEN 'missing_pos_return_refunds' END,
    CASE WHEN NOT has_inventory_ledger THEN 'missing_pos_inventory_ledger' END,
    CASE WHEN NOT has_sync_inbox_events THEN 'missing_pos_sync_inbox_events' END,
    CASE WHEN NOT has_sync_conflicts THEN 'missing_pos_sync_conflicts' END,
    CASE WHEN NOT has_audit_events THEN 'missing_pos_audit_events' END
  ], NULL),
  'sqlWarnings', ARRAY_REMOVE(ARRAY[
    CASE WHEN user_count <= 0 THEN 'pos_users_count_zero_admin_login_validated_by_api' END,
    CASE WHEN retry_pending_sync_count > 0 THEN 'retry_pending_sync_requires_monitoring' END,
    CASE WHEN dead_letter_sync_count > 0 THEN 'dead_letter_sync_requires_triage' END,
    CASE WHEN negative_inventory_item_count > 0 THEN 'negative_inventory_requires_reconciliation' END
  ], NULL),
  'pilot10SqlValidation', CASE WHEN tenant_exists_count = 1 AND store_count > 0 AND terminal_count > 0 AND (has_tenants AND has_stores AND has_terminals AND has_users AND has_sales AND has_payments AND has_cash_shifts AND has_digital_receipts AND has_returns AND has_return_refunds AND has_inventory_ledger AND has_sync_inbox_events AND has_sync_conflicts AND has_audit_events) THEN 'GO' ELSE 'NO-GO' END
)::text
FROM checks, required_tables;
