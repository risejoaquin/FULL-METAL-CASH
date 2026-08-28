WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         :'store_id'::uuid AS store_id,
         :'terminal_id'::uuid AS terminal_id,
         :'sale_id'::uuid AS sale_id,
         :'shift_id'::uuid AS shift_id,
         :'receipt_id'::uuid AS receipt_id,
         :'admin_user_id'::uuid AS admin_user_id,
         :'product_sku'::text AS product_sku,
         :'expected_total_cents'::bigint AS expected_total_cents,
         :'expected_paid_cents'::bigint AS expected_paid_cents,
         :'expected_change_cents'::bigint AS expected_change_cents
), tables AS (
  SELECT to_regclass('pos.tenants') IS NOT NULL AS has_tenants,
         to_regclass('pos.stores') IS NOT NULL AS has_stores,
         to_regclass('pos.user_store_access') IS NOT NULL AS has_user_store_access,
         to_regclass('pos.terminals') IS NOT NULL AS has_terminals,
         to_regclass('pos.sales') IS NOT NULL AS has_sales,
         to_regclass('pos.sale_lines') IS NOT NULL AS has_sale_lines,
         to_regclass('pos.payments') IS NOT NULL AS has_payments,
         to_regclass('pos.cash_shifts') IS NOT NULL AS has_cash_shifts,
         to_regclass('pos.digital_receipts') IS NOT NULL AS has_digital_receipts,
         to_regclass('pos.inventory_ledger') IS NOT NULL AS has_inventory_ledger,
         to_regclass('pos.sync_inbox_events') IS NOT NULL AS has_sync_inbox_events,
         to_regclass('pos.sync_conflicts') IS NOT NULL AS has_sync_conflicts,
         to_regclass('pos.audit_events') IS NOT NULL AS has_audit_events
), inventory_stock AS (
  SELECT l.tenant_id, l.store_id, l.product_id, l.variant_id, l.unit_id, sum(l.quantity_delta) AS quantity_on_hand
  FROM pos.inventory_ledger l
  JOIN params p ON p.tenant_id = l.tenant_id
  GROUP BY l.tenant_id, l.store_id, l.product_id, l.variant_id, l.unit_id
), base AS (
  SELECT p.*,
         (SELECT count(*) FROM pos.tenants t WHERE t.id = p.tenant_id AND t.status = 'active' AND t.deleted_at IS NULL) AS tenant_exists_count,
         (SELECT count(*) FROM pos.stores s WHERE s.tenant_id = p.tenant_id AND s.id = p.store_id AND s.status = 'active' AND s.deleted_at IS NULL) AS store_exists_count,
         (SELECT count(*) FROM pos.user_store_access usa WHERE usa.tenant_id = p.tenant_id AND usa.user_id = p.admin_user_id AND usa.store_id = p.store_id) AS store_access_count,
         (SELECT count(*) FROM pos.terminals tm WHERE tm.tenant_id = p.tenant_id AND tm.store_id = p.store_id AND tm.id = p.terminal_id AND tm.status = 'active' AND tm.deleted_at IS NULL) AS terminal_exists_count,
         (SELECT count(*) FROM pos.sales s WHERE s.tenant_id = p.tenant_id AND s.store_id = p.store_id AND s.terminal_id = p.terminal_id AND s.id = p.sale_id AND s.status = 'completed' AND s.total_cents = p.expected_total_cents AND s.paid_cents = p.expected_paid_cents AND s.change_cents = p.expected_change_cents) AS sale_exists_count,
         (SELECT count(*) FROM pos.sales s JOIN pos.stores st ON st.tenant_id = s.tenant_id AND st.id = s.store_id WHERE s.tenant_id = p.tenant_id AND s.id = p.sale_id AND st.code = 'MAIN') AS main_store_leak_count,
         (SELECT count(*) FROM pos.sale_lines sl JOIN pos.sales s ON s.tenant_id = sl.tenant_id AND s.id = sl.sale_id WHERE sl.tenant_id = p.tenant_id AND s.id = p.sale_id) AS sale_line_count,
         (SELECT count(*) FROM pos.payments pay WHERE pay.tenant_id = p.tenant_id AND pay.sale_id = p.sale_id AND pay.status = 'approved') AS approved_payment_count,
         (SELECT count(*) FROM pos.cash_shifts cs WHERE cs.tenant_id = p.tenant_id AND cs.id = p.shift_id AND cs.store_id = p.store_id AND cs.terminal_id = p.terminal_id AND cs.status = 'closed' AND cs.difference_cents = 0) AS closed_shift_count,
         (SELECT count(*) FROM pos.digital_receipts dr WHERE dr.tenant_id = p.tenant_id AND dr.sale_id = p.sale_id AND dr.id = p.receipt_id AND dr.status = 'active') AS receipt_count,
         (SELECT count(*) FROM pos.inventory_ledger il WHERE il.tenant_id = p.tenant_id AND il.store_id = p.store_id AND il.reference_type = 'sale' AND il.reference_id::text = p.sale_id::text AND il.quantity_delta < 0) AS inventory_ledger_count,
         (SELECT count(*) FROM pos.audit_events ae WHERE ae.tenant_id = p.tenant_id AND ae.entity_id::text = p.sale_id::text AND ae.action = 'sale.completed') AS sale_audit_count,
         (SELECT count(*) FROM pos.sync_conflicts sc WHERE sc.tenant_id = p.tenant_id AND sc.terminal_id = p.terminal_id AND sc.status = 'pending') AS terminal_pending_conflict_count,
         (SELECT count(*) FROM pos.sync_inbox_events sie WHERE sie.tenant_id = p.tenant_id AND sie.terminal_id = p.terminal_id AND sie.status = 'dead_letter') AS terminal_dead_letter_count,
         (SELECT count(*) FROM pos.sales s WHERE s.tenant_id = p.tenant_id AND s.store_id = p.store_id AND s.status = 'completed') AS store_sale_count,
         (SELECT count(*) FROM pos.sales s WHERE s.tenant_id = p.tenant_id AND s.terminal_id = p.terminal_id AND s.status = 'completed') AS terminal_sale_count,
         (SELECT count(*) FROM pos.sync_inbox_events sie WHERE sie.tenant_id = p.tenant_id AND sie.status = 'retry_pending') AS retry_pending_sync_count,
         (SELECT count(*) FROM pos.sync_inbox_events sie WHERE sie.tenant_id = p.tenant_id AND sie.status = 'dead_letter') AS dead_letter_sync_count,
         (SELECT count(*) FROM inventory_stock stock WHERE stock.tenant_id = p.tenant_id AND stock.quantity_on_hand < 0) AS negative_inventory_item_count
  FROM params p
), validation AS (
  SELECT b.*, t.*,
         (t.has_tenants AND t.has_stores AND t.has_user_store_access AND t.has_terminals AND t.has_sales AND t.has_sale_lines AND t.has_payments AND t.has_cash_shifts AND t.has_digital_receipts AND t.has_inventory_ledger AND t.has_sync_inbox_events AND t.has_sync_conflicts AND t.has_audit_events) AS required_tables_present
  FROM base b CROSS JOIN tables t
), reasons AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT required_tables_present THEN 'required_table_missing' END,
    CASE WHEN tenant_exists_count <> 1 THEN 'tenant_missing_or_inactive' END,
    CASE WHEN store_exists_count <> 1 THEN 'second_store_missing_or_inactive' END,
    CASE WHEN store_access_count < 1 THEN 'admin_store_access_missing' END,
    CASE WHEN terminal_exists_count <> 1 THEN 'second_store_terminal_missing_or_inactive' END,
    CASE WHEN sale_exists_count <> 1 THEN 'second_store_sale_missing_or_mismatched' END,
    CASE WHEN main_store_leak_count <> 0 THEN 'second_store_sale_leaked_to_main' END,
    CASE WHEN sale_line_count < 1 THEN 'sale_line_missing' END,
    CASE WHEN approved_payment_count < 1 THEN 'approved_payment_missing' END,
    CASE WHEN closed_shift_count <> 1 THEN 'closed_zero_difference_shift_missing' END,
    CASE WHEN receipt_count <> 1 THEN 'active_receipt_missing' END,
    CASE WHEN inventory_ledger_count < 1 THEN 'inventory_ledger_missing' END,
    CASE WHEN sale_audit_count < 1 THEN 'sale_completed_audit_missing' END,
    CASE WHEN store_sale_count < 1 THEN 'second_store_sale_count_missing' END,
    CASE WHEN terminal_sale_count < 1 THEN 'second_store_terminal_sale_count_missing' END,
    CASE WHEN terminal_pending_conflict_count > 0 THEN 'second_store_terminal_pending_conflict' END,
    CASE WHEN terminal_dead_letter_count > 0 THEN 'second_store_terminal_dead_letter' END
  ], NULL) AS blocking_reasons, v.*
  FROM validation v
)
SELECT json_build_object(
  'requiredTablesPresent', required_tables_present,
  'tenantExistsCount', tenant_exists_count,
  'storeExistsCount', store_exists_count,
  'storeAccessCount', store_access_count,
  'terminalExistsCount', terminal_exists_count,
  'saleExistsCount', sale_exists_count,
  'mainStoreLeakCount', main_store_leak_count,
  'saleLineCount', sale_line_count,
  'approvedPaymentCount', approved_payment_count,
  'closedShiftCount', closed_shift_count,
  'receiptCount', receipt_count,
  'inventoryLedgerCount', inventory_ledger_count,
  'saleAuditCount', sale_audit_count,
  'storeSaleCount', store_sale_count,
  'terminalSaleCount', terminal_sale_count,
  'terminalPendingConflictCount', terminal_pending_conflict_count,
  'terminalDeadLetterCount', terminal_dead_letter_count,
  'retryPendingSyncCount', retry_pending_sync_count,
  'deadLetterSyncCount', dead_letter_sync_count,
  'negativeInventoryItemCount', negative_inventory_item_count,
  'sqlBlockingReasons', blocking_reasons,
  'schemaVersion', 4,
  'exp04SqlValidation', CASE WHEN cardinality(blocking_reasons) = 0 THEN 'GO' ELSE 'NO-GO' END
) FROM reasons;
