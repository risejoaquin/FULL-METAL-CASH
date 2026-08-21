\set ON_ERROR_STOP on

WITH scoped AS (
  SELECT :'tenant_id'::uuid AS tenant_id
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
    (SELECT to_regclass('pos.tenants') IS NOT NULL) AS has_tenants,
    (SELECT to_regclass('pos.stores') IS NOT NULL) AS has_stores,
    (SELECT to_regclass('pos.users') IS NOT NULL) AS has_users,
    (SELECT to_regclass('pos.sales') IS NOT NULL) AS has_sales,
    (SELECT to_regclass('pos.payments') IS NOT NULL) AS has_payments,
    (SELECT to_regclass('pos.inventory_ledger') IS NOT NULL) AS has_inventory_ledger,
    (SELECT to_regclass('pos.sync_inbox_events') IS NOT NULL) AS has_sync_inbox_events,
    (SELECT to_regclass('pos.sync_conflicts') IS NOT NULL) AS has_sync_conflicts,
    (SELECT to_regclass('pos.audit_events') IS NOT NULL) AS has_audit_events,
    COALESCE((SELECT sum(count) FROM sync_counts WHERE status = 'received'), 0)::bigint AS received_count,
    COALESCE((SELECT sum(count) FROM sync_counts WHERE status = 'processing'), 0)::bigint AS processing_count,
    COALESCE((SELECT sum(count) FROM sync_counts WHERE status = 'processed'), 0)::bigint AS processed_count,
    COALESCE((SELECT sum(count) FROM sync_counts WHERE status = 'retry_pending'), 0)::bigint AS retry_pending_count,
    COALESCE((SELECT sum(count) FROM sync_counts WHERE status = 'dead_letter'), 0)::bigint AS dead_letter_count,
    (SELECT count(*)::bigint FROM pos.sync_conflicts c, scoped s WHERE c.tenant_id = s.tenant_id AND c.status = 'pending') AS pending_conflict_count,
    (SELECT count(*)::bigint FROM pos.sync_conflicts c, scoped s WHERE c.tenant_id = s.tenant_id AND c.status = 'resolved') AS resolved_conflict_count,
    (SELECT count(*)::bigint FROM pos.sales sa, scoped s WHERE sa.tenant_id = s.tenant_id AND sa.created_at >= now() - interval '24 hours') AS sales_last_24h,
    (SELECT count(*)::bigint FROM pos.payments p, scoped s WHERE p.tenant_id = s.tenant_id AND p.status IN ('declined', 'voided') AND p.created_at >= now() - interval '24 hours') AS failed_payments_last_24h,
    (SELECT count(*)::bigint FROM pos.payments p, scoped s WHERE p.tenant_id = s.tenant_id AND p.status = 'declined' AND p.created_at >= now() - interval '24 hours') AS declined_payments_last_24h,
    (SELECT count(*)::bigint FROM inventory_stock WHERE quantity_on_hand < 0) AS negative_inventory_item_count,
    (SELECT count(*)::bigint
     FROM inventory_stock stock
     JOIN pos.inventory_low_stock_thresholds t
       ON t.store_id = stock.store_id
      AND t.product_id = stock.product_id
      AND (t.variant_id IS NOT DISTINCT FROM stock.variant_id)
     JOIN scoped s ON s.tenant_id = t.tenant_id
     WHERE stock.quantity_on_hand >= 0 AND stock.quantity_on_hand <= t.reorder_point) AS low_stock_item_count,
    (SELECT count(*)::bigint FROM pos.audit_events a, scoped s WHERE a.tenant_id = s.tenant_id AND a.occurred_at >= now() - interval '24 hours') AS audit_events_last_24h
)
SELECT json_build_object(
  'requiredTablesPresent', (has_tenants AND has_stores AND has_users AND has_sales AND has_payments AND has_inventory_ledger AND has_sync_inbox_events AND has_sync_conflicts AND has_audit_events),
  'receivedCount', received_count,
  'processingCount', processing_count,
  'processedCount', processed_count,
  'retryPendingCount', retry_pending_count,
  'deadLetterCount', dead_letter_count,
  'pendingConflictCount', pending_conflict_count,
  'resolvedConflictCount', resolved_conflict_count,
  'salesLast24Hours', sales_last_24h,
  'failedPaymentsLast24Hours', failed_payments_last_24h,
  'declinedPaymentsLast24Hours', declined_payments_last_24h,
  'negativeInventoryItemCount', negative_inventory_item_count,
  'lowStockItemCount', low_stock_item_count,
  'auditEventsLast24Hours', audit_events_last_24h,
  'pilot07SqlValidation', CASE WHEN (has_tenants AND has_stores AND has_users AND has_sales AND has_payments AND has_inventory_ledger AND has_sync_inbox_events AND has_sync_conflicts AND has_audit_events) THEN 'GO' ELSE 'NO-GO' END
)::text
FROM checks;
