\set ON_ERROR_STOP on
SELECT set_config('app.current_tenant_id', :'tenant_id', false);

WITH p AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         :'ga03_at'::timestamptz AS ga03_at,
         now() AS checked_at
), sale_line_totals AS (
  SELECT sl.tenant_id,sl.sale_id,
         sum(round(sl.quantity * sl.unit_price_cents,0))::bigint AS subtotal_cents,
         sum(sl.discount_cents)::bigint AS discount_cents,
         sum(sl.tax_cents)::bigint AS tax_cents,
         sum(sl.total_cents)::bigint AS line_total_cents
  FROM pos.sale_lines sl JOIN p ON p.tenant_id=sl.tenant_id
  GROUP BY sl.tenant_id,sl.sale_id
), payment_totals AS (
  SELECT py.tenant_id,py.sale_id,
         sum(CASE WHEN lower(coalesce(py.status,''))='approved' THEN py.amount_cents ELSE 0 END)::bigint AS approved_cents,
         count(*) FILTER (WHERE lower(coalesce(py.status,''))='approved')::bigint AS approved_count
  FROM pos.payments py JOIN p ON p.tenant_id=py.tenant_id
  GROUP BY py.tenant_id,py.sale_id
), return_line_totals AS (
  SELECT rl.tenant_id,rl.return_id,sum(rl.total_cents)::bigint AS line_total_cents
  FROM pos.return_lines rl JOIN p ON p.tenant_id=rl.tenant_id
  GROUP BY rl.tenant_id,rl.return_id
), refund_totals AS (
  SELECT rr.tenant_id,rr.return_id,
         sum(CASE WHEN lower(coalesce(rr.status,''))='approved' THEN rr.amount_cents ELSE 0 END)::bigint AS approved_cents
  FROM pos.return_refunds rr JOIN p ON p.tenant_id=rr.tenant_id
  GROUP BY rr.tenant_id,rr.return_id
), inventory_stock AS (
  SELECT l.tenant_id,l.store_id,l.product_id,l.variant_id,l.unit_id,sum(l.quantity_delta) AS quantity_on_hand
  FROM pos.inventory_ledger l JOIN p ON p.tenant_id=l.tenant_id
  GROUP BY l.tenant_id,l.store_id,l.product_id,l.variant_id,l.unit_id
), facts AS (
 SELECT
  EXISTS(SELECT 1 FROM pos.tenants t JOIN p ON p.tenant_id=t.id WHERE t.status='active' AND t.deleted_at IS NULL) AS tenant_active,
  (SELECT count(*) FROM pos.sales s JOIN p ON p.tenant_id=s.tenant_id WHERE s.deleted_at IS NULL AND s.status IN ('completed','partially_returned','returned'))::bigint AS reconciled_sale_count,
  (SELECT count(*) FROM pos.sales s JOIN p ON p.tenant_id=s.tenant_id LEFT JOIN sale_line_totals lt ON lt.tenant_id=s.tenant_id AND lt.sale_id=s.id WHERE s.deleted_at IS NULL AND s.status IN ('completed','partially_returned','returned') AND (lt.sale_id IS NULL OR s.subtotal_cents<>lt.subtotal_cents OR s.discount_cents<>lt.discount_cents OR s.tax_cents<>lt.tax_cents OR s.total_cents<>lt.line_total_cents+s.tip_cents))::bigint AS sale_totals_mismatch_count,
  (SELECT count(*) FROM pos.sales s JOIN p ON p.tenant_id=s.tenant_id LEFT JOIN payment_totals pt ON pt.tenant_id=s.tenant_id AND pt.sale_id=s.id WHERE s.deleted_at IS NULL AND s.status IN ('completed','partially_returned','returned') AND coalesce(pt.approved_cents,0)<>s.paid_cents)::bigint AS sale_payment_mismatch_count,
  (SELECT count(*) FROM pos.sales s JOIN p ON p.tenant_id=s.tenant_id WHERE s.deleted_at IS NULL AND s.status IN ('completed','partially_returned','returned') AND (s.paid_cents<s.total_cents OR s.change_cents<>s.paid_cents-s.total_cents OR s.change_cents<0))::bigint AS sale_tender_mismatch_count,
  (SELECT count(*) FROM pos.payments py JOIN p ON p.tenant_id=py.tenant_id LEFT JOIN pos.sales s ON s.tenant_id=py.tenant_id AND s.id=py.sale_id WHERE lower(coalesce(py.status,''))='approved' AND (s.id IS NULL OR s.deleted_at IS NOT NULL))::bigint AS orphan_approved_payment_count,
  (SELECT count(*) FROM pos.payments py JOIN p ON p.tenant_id=py.tenant_id JOIN pos.sales s ON s.tenant_id=py.tenant_id AND s.id=py.sale_id WHERE lower(coalesce(py.status,''))='approved' AND py.currency<>s.currency)::bigint AS payment_currency_mismatch_count,
  (SELECT count(*) FROM pos.sales s JOIN p ON p.tenant_id=s.tenant_id LEFT JOIN pos.returns r ON r.tenant_id=s.tenant_id AND r.sale_id=s.id AND lower(coalesce(r.status,''))='completed' WHERE s.deleted_at IS NULL AND s.status IN ('partially_returned','returned') AND r.id IS NULL)::bigint AS returned_sale_without_completed_return_count,

  (SELECT count(*) FROM pos.digital_receipts dr JOIN p ON p.tenant_id=dr.tenant_id LEFT JOIN pos.sales s ON s.tenant_id=dr.tenant_id AND s.id=dr.sale_id WHERE lower(coalesce(dr.status,''))='active' AND (s.id IS NULL OR s.deleted_at IS NOT NULL OR s.status NOT IN ('completed','partially_returned','returned')))::bigint AS orphan_active_receipt_count,
  (SELECT count(*) FROM pos.digital_receipts dr JOIN p ON p.tenant_id=dr.tenant_id WHERE lower(coalesce(dr.status,''))='active' AND (length(trim(coalesce(dr.public_token_hash,'')))<16 OR length(trim(coalesce(dr.public_url,'')))<8 OR length(trim(coalesce(dr.receipt_number,'')))<3))::bigint AS invalid_active_receipt_token_count,
  (SELECT count(*) FROM (SELECT dr.public_token_hash FROM pos.digital_receipts dr JOIN p ON p.tenant_id=dr.tenant_id WHERE lower(coalesce(dr.status,''))='active' GROUP BY dr.public_token_hash HAVING count(*)>1) q)::bigint AS duplicate_active_receipt_token_count,
  (SELECT count(*) FROM (SELECT dr.receipt_number FROM pos.digital_receipts dr JOIN p ON p.tenant_id=dr.tenant_id GROUP BY dr.receipt_number HAVING count(*)>1) q)::bigint AS duplicate_receipt_number_count,

  (SELECT count(*) FROM pos.returns r JOIN p ON p.tenant_id=r.tenant_id LEFT JOIN pos.sales s ON s.tenant_id=r.tenant_id AND s.id=r.sale_id WHERE lower(coalesce(r.status,''))='completed' AND (s.id IS NULL OR s.deleted_at IS NOT NULL))::bigint AS orphan_completed_return_count,
  (SELECT count(*) FROM pos.returns r JOIN p ON p.tenant_id=r.tenant_id LEFT JOIN return_line_totals rt ON rt.tenant_id=r.tenant_id AND rt.return_id=r.id WHERE lower(coalesce(r.status,''))='completed' AND (rt.return_id IS NULL OR r.total_cents<>rt.line_total_cents OR r.subtotal_cents+r.tax_cents<>r.total_cents))::bigint AS return_totals_mismatch_count,
  (SELECT count(*) FROM pos.returns r JOIN p ON p.tenant_id=r.tenant_id LEFT JOIN refund_totals rf ON rf.tenant_id=r.tenant_id AND rf.return_id=r.id WHERE lower(coalesce(r.status,''))='completed' AND coalesce(rf.approved_cents,0)<>r.refund_cents)::bigint AS return_refund_mismatch_count,
  (SELECT count(*) FROM pos.return_lines rl JOIN p ON p.tenant_id=rl.tenant_id JOIN pos.returns r ON r.tenant_id=rl.tenant_id AND r.id=rl.return_id LEFT JOIN pos.sale_lines sl ON sl.tenant_id=rl.tenant_id AND sl.id=rl.sale_line_id WHERE sl.id IS NULL OR sl.sale_id<>r.sale_id)::bigint AS invalid_return_line_sale_reference_count,
  (SELECT count(*) FROM pos.return_refunds rr JOIN p ON p.tenant_id=rr.tenant_id LEFT JOIN pos.returns r ON r.tenant_id=rr.tenant_id AND r.id=rr.return_id WHERE lower(coalesce(rr.status,''))='approved' AND (r.id IS NULL OR lower(coalesce(r.status,''))<>'completed'))::bigint AS orphan_approved_refund_count,

  (SELECT count(*) FROM pos.cash_shifts cs JOIN p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='open')::bigint AS open_shift_count,
  (SELECT count(*) FROM pos.cash_shifts cs JOIN p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='open' AND cs.opened_at<p.checked_at-interval '24 hours')::bigint AS stale_open_shift_count,
  (SELECT count(*) FROM pos.cash_shifts cs JOIN p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='closed' AND (cs.counted_cash_cents IS NULL OR cs.difference_cents IS NULL OR cs.difference_cents<>cs.counted_cash_cents-cs.expected_cash_cents))::bigint AS cash_formula_mismatch_count,
  (SELECT count(*) FROM pos.cash_shifts cs JOIN p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='closed' AND coalesce(cs.difference_cents,0)<>0 AND cs.closed_at>=p.checked_at-interval '24 hours')::bigint AS cash_difference_last24h_count,

  (SELECT count(*) FROM inventory_stock st JOIN pos.products pr ON pr.tenant_id=st.tenant_id AND pr.id=st.product_id WHERE pr.deleted_at IS NULL AND pr.is_stock_tracked=true AND pr.allow_negative_stock=false AND st.quantity_on_hand<0)::bigint AS negative_inventory_item_count,
  (SELECT count(*) FROM pos.inventory_ledger l JOIN p ON p.tenant_id=l.tenant_id LEFT JOIN pos.products pr ON pr.tenant_id=l.tenant_id AND pr.id=l.product_id LEFT JOIN pos.stores st ON st.tenant_id=l.tenant_id AND st.id=l.store_id LEFT JOIN pos.units u ON u.tenant_id=l.tenant_id AND u.id=l.unit_id WHERE pr.id IS NULL OR pr.deleted_at IS NOT NULL OR st.id IS NULL OR st.deleted_at IS NOT NULL OR u.id IS NULL)::bigint AS invalid_inventory_reference_count,
  (SELECT count(*) FROM pos.inventory_ledger l JOIN p ON p.tenant_id=l.tenant_id WHERE l.movement_type IN ('sale','sale_recipe_component') AND l.reference_type='sale' AND NOT EXISTS(SELECT 1 FROM pos.sales s WHERE s.tenant_id=l.tenant_id AND s.id=l.reference_id))::bigint AS orphan_sale_inventory_movement_count,
  (SELECT count(*) FROM pos.inventory_ledger l JOIN p ON p.tenant_id=l.tenant_id WHERE l.movement_type='return' AND l.reference_type='return' AND NOT EXISTS(SELECT 1 FROM pos.returns r WHERE r.tenant_id=l.tenant_id AND r.id=l.reference_id))::bigint AS orphan_return_inventory_movement_count,
  (SELECT count(*) FROM pos.recipes r JOIN p ON p.tenant_id=r.tenant_id LEFT JOIN pos.products op ON op.tenant_id=r.tenant_id AND op.id=r.output_product_id LEFT JOIN pos.units yu ON yu.tenant_id=r.tenant_id AND yu.id=r.yield_unit_id WHERE r.deleted_at IS NULL AND lower(coalesce(r.status,''))='active' AND (op.id IS NULL OR op.deleted_at IS NOT NULL OR yu.id IS NULL OR r.yield_quantity<=0))::bigint AS invalid_active_recipe_count,
  (SELECT count(*) FROM pos.recipe_items ri JOIN p ON p.tenant_id=ri.tenant_id JOIN pos.recipes r ON r.tenant_id=ri.tenant_id AND r.id=ri.recipe_id LEFT JOIN pos.products ip ON ip.tenant_id=ri.tenant_id AND ip.id=ri.ingredient_product_id LEFT JOIN pos.units u ON u.tenant_id=ri.tenant_id AND u.id=ri.unit_id WHERE r.deleted_at IS NULL AND lower(coalesce(r.status,''))='active' AND (ip.id IS NULL OR ip.deleted_at IS NOT NULL OR u.id IS NULL OR ri.quantity<=0))::bigint AS invalid_active_recipe_item_count,

  (SELECT count(*) FROM pos.product_prices pp JOIN p ON p.tenant_id=pp.tenant_id LEFT JOIN pos.products pr ON pr.tenant_id=pp.tenant_id AND pr.id=pp.product_id LEFT JOIN pos.price_lists pl ON pl.tenant_id=pp.tenant_id AND pl.id=pp.price_list_id WHERE pp.deleted_at IS NULL AND (pp.price_cents<0 OR pr.id IS NULL OR pr.deleted_at IS NOT NULL OR pl.id IS NULL OR pl.deleted_at IS NOT NULL))::bigint AS invalid_product_price_count,
  (SELECT count(*) FROM pos.product_prices pp JOIN p ON p.tenant_id=pp.tenant_id WHERE pp.deleted_at IS NULL AND pp.starts_at IS NOT NULL AND pp.ends_at IS NOT NULL AND pp.ends_at<=pp.starts_at)::bigint AS invalid_price_window_count,
  (SELECT count(*) FROM pos.products pr JOIN p ON p.tenant_id=pr.tenant_id WHERE pr.deleted_at IS NULL AND pr.tax_mode NOT IN ('taxable','exempt'))::bigint AS invalid_tax_mode_count,
  (SELECT count(*) FROM pos.modifiers m JOIN p ON p.tenant_id=m.tenant_id WHERE m.deleted_at IS NULL AND m.inventory_behavior NOT IN ('none','add','substitute'))::bigint AS invalid_modifier_behavior_count,
  (SELECT count(*) FROM pos.modifiers m JOIN p ON p.tenant_id=m.tenant_id LEFT JOIN pos.products lp ON lp.tenant_id=m.tenant_id AND lp.id=m.linked_product_id LEFT JOIN pos.products rp ON rp.tenant_id=m.tenant_id AND rp.id=m.replaces_product_id WHERE m.deleted_at IS NULL AND ((m.inventory_behavior='add' AND (m.linked_product_id IS NULL OR lp.id IS NULL OR m.consumption_quantity IS NULL OR m.consumption_quantity<=0 OR m.consumption_unit_id IS NULL)) OR (m.inventory_behavior='substitute' AND (m.linked_product_id IS NULL OR lp.id IS NULL OR m.replaces_product_id IS NULL OR rp.id IS NULL OR m.linked_product_id=m.replaces_product_id OR m.consumption_quantity IS NULL OR m.consumption_quantity<=0 OR m.consumption_unit_id IS NULL)) OR (m.inventory_behavior='none' AND (m.consumption_quantity IS NOT NULL OR m.consumption_unit_id IS NOT NULL OR m.replaces_product_id IS NOT NULL))))::bigint AS invalid_modifier_semantics_count,

  (SELECT count(*) FROM pos.user_roles ur JOIN p ON p.tenant_id=ur.tenant_id LEFT JOIN pos.users u ON u.tenant_id=ur.tenant_id AND u.id=ur.user_id LEFT JOIN pos.roles r ON r.tenant_id=ur.tenant_id AND r.id=ur.role_id WHERE u.id IS NULL OR u.deleted_at IS NOT NULL OR r.id IS NULL OR r.deleted_at IS NOT NULL)::bigint AS orphan_user_role_count,
  (SELECT count(*) FROM pos.user_store_access usa JOIN p ON p.tenant_id=usa.tenant_id LEFT JOIN pos.users u ON u.tenant_id=usa.tenant_id AND u.id=usa.user_id LEFT JOIN pos.stores s ON s.tenant_id=usa.tenant_id AND s.id=usa.store_id WHERE u.id IS NULL OR u.deleted_at IS NOT NULL OR s.id IS NULL OR s.deleted_at IS NOT NULL)::bigint AS orphan_store_access_count,
  (SELECT count(*) FROM pos.user_store_access usa JOIN p ON p.tenant_id=usa.tenant_id JOIN pos.users u ON u.tenant_id=usa.tenant_id AND u.id=usa.user_id JOIN pos.stores s ON s.tenant_id=usa.tenant_id AND s.id=usa.store_id WHERE u.status<>'active' OR s.status<>'active')::bigint AS inactive_access_relationship_count,

  (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='retry_pending')::bigint AS retry_pending_count,
  (SELECT count(*) FROM pos.sync_conflicts c JOIN p ON p.tenant_id=c.tenant_id WHERE lower(coalesce(c.status,''))='pending')::bigint AS pending_conflict_count,
  (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE coalesce(i.schema_version,4)<>4)::bigint AS legacy_schema_event_count,
  (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='dead_letter' AND coalesce(i.dead_lettered_at,i.created_at)>=p.ga03_at)::bigint AS new_dead_letter_since_ga03_count,
  (SELECT count(*) FROM pos.audit_events a JOIN p ON p.tenant_id=a.tenant_id)::bigint AS audit_event_count,
  (SELECT count(*) FROM pos.audit_events a JOIN p ON p.tenant_id=a.tenant_id WHERE a.occurred_at>=p.checked_at-interval '24 hours')::bigint AS audit_events_last24h,
  (SELECT count(*) FROM pos.audit_events a JOIN p ON p.tenant_id=a.tenant_id WHERE a.action='ga02.dead_letter_closed_as_historical_evidence' AND coalesce(a.after_data->>'decision','')='close_as_historical_evidence')::bigint AS historical_dead_letter_decision_audit_count
), blockers AS (
 SELECT array_remove(ARRAY[
  CASE WHEN NOT tenant_active THEN 'tenant_missing_or_inactive' END,
  CASE WHEN reconciled_sale_count<1 THEN 'sales_reconciliation_evidence_missing' END,
  CASE WHEN sale_totals_mismatch_count>0 THEN 'sale_totals_mismatch' END,
  CASE WHEN sale_payment_mismatch_count>0 THEN 'sale_payment_mismatch' END,
  CASE WHEN sale_tender_mismatch_count>0 THEN 'sale_tender_mismatch' END,
  CASE WHEN orphan_approved_payment_count>0 THEN 'orphan_approved_payment' END,
  CASE WHEN payment_currency_mismatch_count>0 THEN 'payment_currency_mismatch' END,
  CASE WHEN returned_sale_without_completed_return_count>0 THEN 'returned_sale_without_completed_return' END,
  CASE WHEN orphan_active_receipt_count>0 THEN 'orphan_active_receipt' END,
  CASE WHEN invalid_active_receipt_token_count>0 THEN 'invalid_active_receipt_token' END,
  CASE WHEN duplicate_active_receipt_token_count>0 THEN 'duplicate_active_receipt_token' END,
  CASE WHEN duplicate_receipt_number_count>0 THEN 'duplicate_receipt_number' END,
  CASE WHEN orphan_completed_return_count>0 THEN 'orphan_completed_return' END,
  CASE WHEN return_totals_mismatch_count>0 THEN 'return_totals_mismatch' END,
  CASE WHEN return_refund_mismatch_count>0 THEN 'return_refund_mismatch' END,
  CASE WHEN invalid_return_line_sale_reference_count>0 THEN 'invalid_return_line_sale_reference' END,
  CASE WHEN orphan_approved_refund_count>0 THEN 'orphan_approved_refund' END,
  CASE WHEN open_shift_count>0 THEN 'open_cash_shift' END,
  CASE WHEN stale_open_shift_count>0 THEN 'stale_open_cash_shift' END,
  CASE WHEN cash_formula_mismatch_count>0 THEN 'cash_formula_mismatch' END,
  CASE WHEN cash_difference_last24h_count>0 THEN 'cash_difference_last24h' END,
  CASE WHEN negative_inventory_item_count>0 THEN 'negative_inventory_item' END,
  CASE WHEN invalid_inventory_reference_count>0 THEN 'invalid_inventory_reference' END,
  CASE WHEN orphan_sale_inventory_movement_count>0 THEN 'orphan_sale_inventory_movement' END,
  CASE WHEN orphan_return_inventory_movement_count>0 THEN 'orphan_return_inventory_movement' END,
  CASE WHEN invalid_active_recipe_count>0 THEN 'invalid_active_recipe' END,
  CASE WHEN invalid_active_recipe_item_count>0 THEN 'invalid_active_recipe_item' END,
  CASE WHEN invalid_product_price_count>0 THEN 'invalid_product_price' END,
  CASE WHEN invalid_price_window_count>0 THEN 'invalid_price_window' END,
  CASE WHEN invalid_tax_mode_count>0 THEN 'invalid_tax_mode' END,
  CASE WHEN invalid_modifier_behavior_count>0 THEN 'invalid_modifier_behavior' END,
  CASE WHEN invalid_modifier_semantics_count>0 THEN 'invalid_modifier_semantics' END,
  CASE WHEN orphan_user_role_count>0 THEN 'orphan_user_role' END,
  CASE WHEN orphan_store_access_count>0 THEN 'orphan_store_access' END,
  CASE WHEN inactive_access_relationship_count>0 THEN 'inactive_user_or_store_access_relationship' END,
  CASE WHEN retry_pending_count>0 THEN 'retry_pending_sync_reopened' END,
  CASE WHEN pending_conflict_count>0 THEN 'pending_sync_conflict' END,
  CASE WHEN legacy_schema_event_count>0 THEN 'legacy_schema_event' END,
  CASE WHEN new_dead_letter_since_ga03_count>0 THEN 'new_dead_letter_since_ga03' END,
  CASE WHEN audit_event_count<1 OR audit_events_last24h<1 THEN 'audit_evidence_missing' END,
  CASE WHEN historical_dead_letter_decision_audit_count<1 THEN 'historical_dead_letter_decision_evidence_missing' END
 ],NULL) AS items
 FROM facts
)
SELECT (
 jsonb_build_object(
  'ga04SqlContract','ga_production_data_integrity_financial_reconciliation',
  'tenantActive',tenant_active,
  'reconciledSaleCount',reconciled_sale_count,
  'saleTotalsMismatchCount',sale_totals_mismatch_count,
  'salePaymentMismatchCount',sale_payment_mismatch_count,
  'saleTenderMismatchCount',sale_tender_mismatch_count,
  'orphanApprovedPaymentCount',orphan_approved_payment_count,
  'paymentCurrencyMismatchCount',payment_currency_mismatch_count,
  'returnedSaleWithoutCompletedReturnCount',returned_sale_without_completed_return_count,
  'orphanActiveReceiptCount',orphan_active_receipt_count,
  'invalidActiveReceiptTokenCount',invalid_active_receipt_token_count,
  'duplicateActiveReceiptTokenCount',duplicate_active_receipt_token_count,
  'duplicateReceiptNumberCount',duplicate_receipt_number_count,
  'orphanCompletedReturnCount',orphan_completed_return_count,
  'returnTotalsMismatchCount',return_totals_mismatch_count,
  'returnRefundMismatchCount',return_refund_mismatch_count,
  'invalidReturnLineSaleReferenceCount',invalid_return_line_sale_reference_count,
  'orphanApprovedRefundCount',orphan_approved_refund_count,
  'openShiftCount',open_shift_count,
  'staleOpenShiftCount',stale_open_shift_count,
  'cashFormulaMismatchCount',cash_formula_mismatch_count,
  'cashDifferenceLast24HoursCount',cash_difference_last24h_count
 ) || jsonb_build_object(
  'negativeInventoryItemCount',negative_inventory_item_count,
  'invalidInventoryReferenceCount',invalid_inventory_reference_count,
  'orphanSaleInventoryMovementCount',orphan_sale_inventory_movement_count,
  'orphanReturnInventoryMovementCount',orphan_return_inventory_movement_count,
  'invalidActiveRecipeCount',invalid_active_recipe_count,
  'invalidActiveRecipeItemCount',invalid_active_recipe_item_count,
  'invalidProductPriceCount',invalid_product_price_count,
  'invalidPriceWindowCount',invalid_price_window_count,
  'invalidTaxModeCount',invalid_tax_mode_count,
  'invalidModifierBehaviorCount',invalid_modifier_behavior_count,
  'invalidModifierSemanticsCount',invalid_modifier_semantics_count,
  'orphanUserRoleCount',orphan_user_role_count,
  'orphanStoreAccessCount',orphan_store_access_count,
  'inactiveAccessRelationshipCount',inactive_access_relationship_count,
  'retryPendingCount',retry_pending_count,
  'pendingConflictCount',pending_conflict_count,
  'legacySchemaEventCount',legacy_schema_event_count,
  'newDeadLetterSinceGa03Count',new_dead_letter_since_ga03_count,
  'auditEventCount',audit_event_count,
  'auditEventsLast24Hours',audit_events_last24h,
  'historicalDeadLetterDecisionAuditCount',historical_dead_letter_decision_audit_count,
  'blockers',blockers.items,
  'ga04SqlDecision',CASE WHEN cardinality(blockers.items)=0 THEN 'GO' ELSE 'NO-GO' END,
  'schemaVersion',4,
  'syncContract','schema_version_4',
  'generalAvailabilityActivated',false,
  'checkedAt',checked_at
 )
)::text
FROM facts CROSS JOIN blockers CROSS JOIN p;
