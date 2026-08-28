\set ON_ERROR_STOP on
SELECT set_config('app.current_tenant_id', :'tenant_id', false);

WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id, now() AS checked_at, interval '24 hours' AS window_24h
), inventory_stock AS (
  SELECT l.store_id,l.product_id,l.variant_id,sum(l.quantity_delta) AS quantity_on_hand
  FROM pos.inventory_ledger l JOIN params p ON p.tenant_id=l.tenant_id
  GROUP BY l.store_id,l.product_id,l.variant_id
), facts AS (
  SELECT
    EXISTS(SELECT 1 FROM pos.tenants t JOIN params p ON p.tenant_id=t.id WHERE t.status='active' AND t.deleted_at IS NULL) AS tenant_active,
    (SELECT count(*) FROM pos.stores s JOIN params p ON p.tenant_id=s.tenant_id WHERE s.status='active' AND s.deleted_at IS NULL) AS active_store_count,
    (SELECT count(*) FROM pos.terminals t JOIN params p ON p.tenant_id=t.tenant_id WHERE t.status='active' AND t.deleted_at IS NULL) AS active_terminal_count,
    (SELECT count(*) FROM pos.sales s JOIN params p ON p.tenant_id=s.tenant_id WHERE s.status IN ('completed','returned') AND s.deleted_at IS NULL) AS accepted_sale_count,
    (SELECT count(*) FROM pos.payments py JOIN params p ON p.tenant_id=py.tenant_id WHERE lower(coalesce(py.status,''))='approved') AS approved_payment_count,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN params p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='closed') AS closed_shift_count,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN params p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='closed' AND coalesce(cs.difference_cents,0)<>0 AND cs.closed_at>=p.checked_at-p.window_24h) AS cash_difference_24h,
    (SELECT count(*) FROM pos.digital_receipts dr JOIN params p ON p.tenant_id=dr.tenant_id WHERE lower(coalesce(dr.status,''))='active') AS active_receipt_count,
    (SELECT count(*) FROM pos.returns r JOIN params p ON p.tenant_id=r.tenant_id WHERE lower(coalesce(r.status,''))='completed') AS completed_return_count,
    (SELECT count(*) FROM pos.return_refunds rr JOIN params p ON p.tenant_id=rr.tenant_id WHERE lower(coalesce(rr.status,''))='approved') AS approved_refund_count,
    (SELECT count(*) FROM pos.products pr JOIN params p ON p.tenant_id=pr.tenant_id WHERE pr.status='active' AND pr.deleted_at IS NULL) AS active_product_count,
    (SELECT count(*) FROM pos.product_prices pp JOIN params p ON p.tenant_id=pp.tenant_id) AS product_price_count,
    (SELECT count(*) FROM pos.users u JOIN params p ON p.tenant_id=u.tenant_id WHERE u.status='active' AND u.deleted_at IS NULL) AS active_user_count,
    (SELECT count(*) FROM pos.user_roles ur JOIN params p ON p.tenant_id=ur.tenant_id) AS role_assignment_count,
    (SELECT count(*) FROM pos.user_store_access usa JOIN params p ON p.tenant_id=usa.tenant_id) AS store_access_assignment_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN params p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='processed' AND coalesce(i.schema_version,4)=4) AS processed_schema4_sync_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN params p ON p.tenant_id=i.tenant_id WHERE coalesce(i.schema_version,4)<>4) AS legacy_schema_event_count,
    (SELECT count(*) FROM pos.sync_conflicts c JOIN params p ON p.tenant_id=c.tenant_id WHERE lower(coalesce(c.status,''))='pending') AS pending_conflict_count,
    (SELECT count(*) FROM pos.audit_events a JOIN params p ON p.tenant_id=a.tenant_id) AS audit_event_count,
    (SELECT count(*) FROM pos.update_releases r JOIN params p ON p.tenant_id=r.tenant_id WHERE r.channel='beta' AND r.revoked_at IS NULL AND r.package_type='velopack' AND r.mandatory=false AND length(coalesce(r.signature,''))>=8 AND length(coalesce(r.rollback_version,''))>0) AS active_beta_release_count,
    (SELECT count(*) FROM inventory_stock st WHERE st.quantity_on_hand<0) AS negative_inventory_item_count
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT tenant_active THEN 'tenant_missing_or_inactive' END,
    CASE WHEN active_store_count<1 THEN 'active_store_missing' END,
    CASE WHEN active_terminal_count<1 THEN 'active_terminal_missing' END,
    CASE WHEN accepted_sale_count<1 OR approved_payment_count<1 THEN 'sales_acceptance_evidence_missing' END,
    CASE WHEN closed_shift_count<1 OR cash_difference_24h>0 THEN 'cash_acceptance_failed' END,
    CASE WHEN active_receipt_count<1 THEN 'receipts_acceptance_evidence_missing' END,
    CASE WHEN completed_return_count<1 OR approved_refund_count<1 THEN 'returns_refunds_acceptance_evidence_missing' END,
    CASE WHEN active_product_count<1 OR product_price_count<1 THEN 'catalog_pricing_acceptance_evidence_missing' END,
    CASE WHEN active_user_count<1 OR role_assignment_count<1 OR store_access_assignment_count<1 THEN 'user_admin_acceptance_evidence_missing' END,
    CASE WHEN processed_schema4_sync_count<1 OR pending_conflict_count>0 OR legacy_schema_event_count>0 THEN 'offline_acceptance_failed' END,
    CASE WHEN audit_event_count<1 THEN 'support_acceptance_evidence_missing' END,
    CASE WHEN active_beta_release_count<1 THEN 'release_update_acceptance_evidence_missing' END
  ],NULL) AS items,
  array_remove(ARRAY[
    CASE WHEN negative_inventory_item_count>0 THEN 'known_issue_negative_inventory_requires_reconciliation' END
  ],NULL) AS known_issues
  FROM facts
)
SELECT json_build_object(
  'beta08SqlDecision',CASE WHEN cardinality(blockers.items)=0 THEN 'GO' ELSE 'NO-GO' END,
  'blockers',blockers.items,
  'knownIssues',blockers.known_issues,
  'tenantActive',tenant_active,
  'activeStoreCount',active_store_count,
  'activeTerminalCount',active_terminal_count,
  'acceptedSaleCount',accepted_sale_count,
  'approvedPaymentCount',approved_payment_count,
  'closedShiftCount',closed_shift_count,
  'cashDifferenceLast24HoursCount',cash_difference_24h,
  'activeReceiptCount',active_receipt_count,
  'completedReturnCount',completed_return_count,
  'approvedRefundCount',approved_refund_count,
  'activeProductCount',active_product_count,
  'productPriceCount',product_price_count,
  'activeUserCount',active_user_count,
  'roleAssignmentCount',role_assignment_count,
  'storeAccessAssignmentCount',store_access_assignment_count,
  'processedSchema4SyncCount',processed_schema4_sync_count,
  'pendingConflictCount',pending_conflict_count,
  'legacySchemaEventCount',legacy_schema_event_count,
  'auditEventCount',audit_event_count,
  'activeBetaReleaseCount',active_beta_release_count,
  'negativeInventoryItemCount',negative_inventory_item_count,
  'schemaVersion',4,
  'syncContract','schema_version_4'
)::text
FROM facts CROSS JOIN blockers;
