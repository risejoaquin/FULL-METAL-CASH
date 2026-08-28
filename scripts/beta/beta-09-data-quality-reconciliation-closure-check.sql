\set ON_ERROR_STOP on
SELECT set_config('app.current_tenant_id', :'tenant_id', false);

WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         :'baseline_at'::timestamptz AS baseline_at,
         now() AS checked_at
), inventory_stock AS (
  SELECT l.store_id,l.product_id,l.variant_id,l.unit_id,sum(l.quantity_delta) AS quantity_on_hand
  FROM pos.inventory_ledger l JOIN params p ON p.tenant_id=l.tenant_id
  GROUP BY l.store_id,l.product_id,l.variant_id,l.unit_id
), payment_by_sale AS (
  SELECT py.sale_id,sum(py.amount_cents)::bigint AS approved_payment_cents
  FROM pos.payments py JOIN params p ON p.tenant_id=py.tenant_id
  WHERE lower(coalesce(py.status,''))='approved'
  GROUP BY py.sale_id
), refund_by_return AS (
  SELECT rr.return_id,sum(rr.amount_cents)::bigint AS approved_refund_cents
  FROM pos.return_refunds rr JOIN params p ON p.tenant_id=rr.tenant_id
  WHERE lower(coalesce(rr.status,''))='approved'
  GROUP BY rr.return_id
), facts AS (
  SELECT
    EXISTS(SELECT 1 FROM pos.tenants t JOIN params p ON p.tenant_id=t.id WHERE t.status='active' AND t.deleted_at IS NULL) AS tenant_active,
    (SELECT count(*) FROM pos.sales s JOIN params p ON p.tenant_id=s.tenant_id WHERE s.status IN ('completed','returned') AND s.deleted_at IS NULL) AS reconciled_sale_count,
    (SELECT count(*) FROM pos.sales s JOIN params p ON p.tenant_id=s.tenant_id LEFT JOIN payment_by_sale pb ON pb.sale_id=s.id WHERE s.status IN ('completed','returned') AND s.deleted_at IS NULL AND coalesce(pb.approved_payment_cents,0)<>coalesce(s.paid_cents,0)) AS sale_payment_mismatch_count,
    (SELECT count(*) FROM pos.digital_receipts dr JOIN params p ON p.tenant_id=dr.tenant_id LEFT JOIN pos.sales s ON s.tenant_id=dr.tenant_id AND s.id=dr.sale_id WHERE lower(coalesce(dr.status,''))='active' AND (s.id IS NULL OR s.deleted_at IS NOT NULL)) AS orphan_active_receipt_count,
    (SELECT count(*) FROM pos.returns r JOIN params p ON p.tenant_id=r.tenant_id LEFT JOIN refund_by_return rb ON rb.return_id=r.id WHERE lower(coalesce(r.status,''))='completed' AND coalesce(rb.approved_refund_cents,0)<>coalesce(r.refund_cents,0)) AS return_refund_mismatch_count,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN params p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='closed' AND coalesce(cs.difference_cents,0)<>0 AND cs.closed_at>=p.checked_at-interval '24 hours') AS cash_difference_last_24h_count,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN params p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='open') AS open_shift_count,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN params p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='open' AND cs.opened_at<p.checked_at-interval '24 hours') AS stale_open_shift_count,
    (SELECT count(*) FROM inventory_stock st WHERE st.quantity_on_hand<0) AS negative_inventory_item_count,
    (SELECT count(*) FROM pos.product_prices pp JOIN params p ON p.tenant_id=pp.tenant_id WHERE pp.deleted_at IS NULL AND pp.price_cents<0) AS negative_price_count,
    (SELECT count(*) FROM pos.product_prices pp JOIN params p ON p.tenant_id=pp.tenant_id WHERE pp.deleted_at IS NULL AND pp.starts_at IS NOT NULL AND pp.ends_at IS NOT NULL AND pp.ends_at<=pp.starts_at) AS invalid_price_window_count,
    (SELECT count(*) FROM pos.products pr JOIN params p ON p.tenant_id=pr.tenant_id WHERE pr.deleted_at IS NULL AND pr.tax_mode NOT IN ('taxable','exempt')) AS invalid_tax_mode_count,
    (SELECT count(*) FROM pos.modifiers m JOIN params p ON p.tenant_id=m.tenant_id WHERE m.deleted_at IS NULL AND m.inventory_behavior NOT IN ('none','add','substitute')) AS invalid_modifier_behavior_count,
    (SELECT count(*) FROM pos.modifiers m JOIN params p ON p.tenant_id=m.tenant_id WHERE m.deleted_at IS NULL AND m.inventory_behavior='substitute' AND (m.replaces_product_id IS NULL OR m.linked_product_id IS NULL OR m.consumption_quantity IS NULL OR m.consumption_quantity<=0 OR m.consumption_unit_id IS NULL)) AS invalid_substitute_modifier_count,
    (SELECT count(*) FROM pos.users u JOIN params p ON p.tenant_id=u.tenant_id WHERE u.status='active' AND u.deleted_at IS NULL) AS active_user_count,
    (SELECT count(*) FROM pos.customers c JOIN params p ON p.tenant_id=c.tenant_id WHERE c.deleted_at IS NULL) AS customer_count,
    (SELECT count(*) FROM pos.user_roles ur JOIN params p ON p.tenant_id=ur.tenant_id LEFT JOIN pos.users u ON u.tenant_id=ur.tenant_id AND u.id=ur.user_id WHERE u.id IS NULL OR u.deleted_at IS NOT NULL) AS orphan_user_role_count,
    (SELECT count(*) FROM pos.user_store_access usa JOIN params p ON p.tenant_id=usa.tenant_id LEFT JOIN pos.users u ON u.tenant_id=usa.tenant_id AND u.id=usa.user_id LEFT JOIN pos.stores s ON s.tenant_id=usa.tenant_id AND s.id=usa.store_id WHERE u.id IS NULL OR s.id IS NULL OR u.deleted_at IS NOT NULL OR s.deleted_at IS NOT NULL) AS orphan_store_access_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN params p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='processed' AND coalesce(i.schema_version,4)=4) AS processed_schema4_sync_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN params p ON p.tenant_id=i.tenant_id WHERE coalesce(i.schema_version,4)<>4) AS legacy_schema_event_count,
    (SELECT count(*) FROM pos.sync_conflicts c JOIN params p ON p.tenant_id=c.tenant_id WHERE lower(coalesce(c.status,''))='pending') AS unresolved_conflict_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN params p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='dead_letter') AS dead_letter_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN params p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='dead_letter' AND coalesce(i.dead_lettered_at,i.created_at)>=p.baseline_at) AS new_dead_letter_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN params p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='dead_letter' AND (i.dead_lettered_at IS NULL OR length(coalesce(i.error_code,''))=0 OR length(coalesce(i.error_message,''))=0)) AS untriaged_dead_letter_count,
    (SELECT count(*) FROM pos.audit_events a JOIN params p ON p.tenant_id=a.tenant_id) AS audit_event_count,
    (SELECT count(*) FROM pos.audit_events a JOIN params p ON p.tenant_id=a.tenant_id WHERE a.occurred_at>=p.checked_at-interval '24 hours') AS audit_events_last_24h,
    (SELECT count(*) FROM pos.update_releases r JOIN params p ON p.tenant_id=r.tenant_id WHERE r.channel='beta' AND r.revoked_at IS NULL AND r.package_type='velopack' AND r.mandatory=false AND length(coalesce(r.signature,''))>=8 AND length(coalesce(r.rollback_version,''))>0) AS active_beta_release_count,
    (SELECT count(*) FROM pos.update_releases r JOIN params p ON p.tenant_id=r.tenant_id WHERE r.channel='beta' AND r.revoked_at IS NULL AND (r.package_type<>'velopack' OR r.mandatory=true OR length(coalesce(r.signature,''))<8 OR length(coalesce(r.rollback_version,''))=0)) AS invalid_beta_release_count
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT tenant_active THEN 'tenant_missing_or_inactive' END,
    CASE WHEN reconciled_sale_count<1 THEN 'sales_reconciliation_evidence_missing' END,
    CASE WHEN sale_payment_mismatch_count>0 THEN 'sale_payment_reconciliation_mismatch' END,
    CASE WHEN orphan_active_receipt_count>0 THEN 'orphan_active_receipt_detected' END,
    CASE WHEN return_refund_mismatch_count>0 THEN 'return_refund_reconciliation_mismatch' END,
    CASE WHEN cash_difference_last_24h_count>0 THEN 'cash_difference_requires_review_before_closure' END,
    CASE WHEN stale_open_shift_count>0 THEN 'stale_open_shift_requires_review_before_closure' END,
    CASE WHEN negative_inventory_item_count>0 THEN 'negative_inventory_remaining_after_reconciliation' END,
    CASE WHEN negative_price_count>0 THEN 'negative_product_price_detected' END,
    CASE WHEN invalid_price_window_count>0 THEN 'invalid_price_window_detected' END,
    CASE WHEN invalid_tax_mode_count>0 THEN 'invalid_tax_mode_detected' END,
    CASE WHEN invalid_modifier_behavior_count>0 THEN 'invalid_modifier_behavior_detected' END,
    CASE WHEN invalid_substitute_modifier_count>0 THEN 'invalid_substitute_modifier_detected' END,
    CASE WHEN active_user_count<1 THEN 'active_user_missing' END,
    CASE WHEN orphan_user_role_count>0 THEN 'orphan_user_role_detected' END,
    CASE WHEN orphan_store_access_count>0 THEN 'orphan_store_access_detected' END,
    CASE WHEN processed_schema4_sync_count<1 THEN 'schema4_sync_evidence_missing' END,
    CASE WHEN legacy_schema_event_count>0 THEN 'legacy_sync_schema_event_detected' END,
    CASE WHEN unresolved_conflict_count>0 THEN 'unresolved_sync_conflict_detected' END,
    CASE WHEN new_dead_letter_count>0 THEN 'new_dead_letter_requires_triage' END,
    CASE WHEN untriaged_dead_letter_count>0 THEN 'dead_letter_diagnostic_evidence_incomplete' END,
    CASE WHEN audit_event_count<1 OR audit_events_last_24h<1 THEN 'audit_consistency_evidence_missing' END,
    CASE WHEN active_beta_release_count<1 THEN 'active_beta_release_missing' END,
    CASE WHEN invalid_beta_release_count>0 THEN 'invalid_active_beta_release_detected' END
  ],NULL) AS items,
  array_remove(ARRAY[
    CASE WHEN open_shift_count>0 AND stale_open_shift_count=0 THEN 'open_cash_shift_reviewed_within_daily_window' END,
    CASE WHEN dead_letter_count>0 AND new_dead_letter_count=0 AND untriaged_dead_letter_count=0 THEN 'known_dead_letter_triaged_and_stable' END,
    CASE WHEN customer_count=0 THEN 'customer_dataset_empty_review' END
  ],NULL) AS conditions
  FROM facts
)
SELECT json_build_object(
  'beta09SqlDecision',CASE WHEN cardinality(blockers.items)=0 THEN 'GO' ELSE 'NO-GO' END,
  'blockers',blockers.items,
  'conditions',blockers.conditions,
  'tenantActive',tenant_active,
  'reconciledSaleCount',reconciled_sale_count,
  'salePaymentMismatchCount',sale_payment_mismatch_count,
  'orphanActiveReceiptCount',orphan_active_receipt_count,
  'returnRefundMismatchCount',return_refund_mismatch_count,
  'cashDifferenceLast24HoursCount',cash_difference_last_24h_count,
  'openShiftCount',open_shift_count,
  'staleOpenShiftCount',stale_open_shift_count,
  'negativeInventoryItemCount',negative_inventory_item_count,
  'negativePriceCount',negative_price_count,
  'invalidPriceWindowCount',invalid_price_window_count,
  'invalidTaxModeCount',invalid_tax_mode_count,
  'invalidModifierBehaviorCount',invalid_modifier_behavior_count,
  'invalidSubstituteModifierCount',invalid_substitute_modifier_count,
  'activeUserCount',active_user_count,
  'customerCount',customer_count,
  'orphanUserRoleCount',orphan_user_role_count,
  'orphanStoreAccessCount',orphan_store_access_count,
  'processedSchema4SyncCount',processed_schema4_sync_count,
  'legacySchemaEventCount',legacy_schema_event_count,
  'unresolvedConflictCount',unresolved_conflict_count,
  'deadLetterCount',dead_letter_count,
  'newDeadLetterCount',new_dead_letter_count,
  'untriagedDeadLetterCount',untriaged_dead_letter_count,
  'auditEventCount',audit_event_count,
  'auditEventsLast24Hours',audit_events_last_24h,
  'activeBetaReleaseCount',active_beta_release_count,
  'invalidBetaReleaseCount',invalid_beta_release_count,
  'baselineAt',baseline_at,
  'schemaVersion',4,
  'syncContract','schema_version_4'
)::text
FROM facts CROSS JOIN blockers CROSS JOIN params;
