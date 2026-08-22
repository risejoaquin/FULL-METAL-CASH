\set ON_ERROR_STOP on
SELECT set_config('app.tenant_id', :'tenant_id', false);

WITH p AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         now() AS checked_at,
         :'beta10_at'::timestamptz AS beta10_at
), inventory AS (
  SELECT l.tenant_id, l.product_id, sum(l.quantity_delta) AS quantity
  FROM pos.inventory_ledger l
  JOIN p ON p.tenant_id = l.tenant_id
  GROUP BY l.tenant_id, l.product_id
), facts AS (
  SELECT
    (SELECT count(*) FROM pos.tenants t JOIN p ON p.tenant_id=t.id WHERE t.deleted_at IS NULL)::bigint AS tenant_count,
    (SELECT count(*) FROM pos.tenants t JOIN p ON p.tenant_id=t.id WHERE t.status='active' AND t.deleted_at IS NULL)::bigint AS active_tenant_count,
    (SELECT count(*) FROM pos.stores s JOIN p ON p.tenant_id=s.tenant_id WHERE s.deleted_at IS NULL)::bigint AS store_count,
    (SELECT count(*) FROM pos.stores s JOIN p ON p.tenant_id=s.tenant_id WHERE s.status='active' AND s.deleted_at IS NULL)::bigint AS active_store_count,
    (SELECT count(*) FROM pos.terminals t JOIN p ON p.tenant_id=t.tenant_id WHERE t.deleted_at IS NULL)::bigint AS terminal_count,
    (SELECT count(*) FROM pos.terminals t JOIN p ON p.tenant_id=t.tenant_id WHERE t.status='active' AND t.deleted_at IS NULL)::bigint AS active_terminal_count,
    (SELECT count(*) FROM pos.users u JOIN p ON p.tenant_id=u.tenant_id WHERE u.deleted_at IS NULL)::bigint AS user_count,
    (SELECT count(*) FROM pos.users u JOIN p ON p.tenant_id=u.tenant_id WHERE u.status='active' AND u.deleted_at IS NULL)::bigint AS active_user_count,
    (SELECT count(*) FROM pos.customers c JOIN p ON p.tenant_id=c.tenant_id WHERE c.deleted_at IS NULL)::bigint AS customer_count,
    (SELECT count(*) FROM pos.products pr JOIN p ON p.tenant_id=pr.tenant_id WHERE pr.deleted_at IS NULL)::bigint AS product_count,
    (SELECT count(*) FROM pos.product_prices pp JOIN p ON p.tenant_id=pp.tenant_id WHERE pp.deleted_at IS NULL)::bigint AS product_price_count,
    (SELECT count(*) FROM pos.modifiers m JOIN p ON p.tenant_id=m.tenant_id WHERE m.deleted_at IS NULL)::bigint AS modifier_count,
    (SELECT count(*) FROM pos.modifiers m JOIN p ON p.tenant_id=m.tenant_id WHERE m.deleted_at IS NULL AND m.inventory_behavior NOT IN ('none','add','substitute'))::bigint AS invalid_modifier_behavior_count,
    (SELECT count(*) FROM pos.modifiers m JOIN p ON p.tenant_id=m.tenant_id WHERE m.deleted_at IS NULL AND m.inventory_behavior='substitute' AND (m.replaces_product_id IS NULL OR m.linked_product_id IS NULL OR m.consumption_quantity IS NULL OR m.consumption_quantity<=0 OR m.consumption_unit_id IS NULL))::bigint AS invalid_substitute_modifier_count,
    (SELECT count(*) FROM pos.sales s JOIN p ON p.tenant_id=s.tenant_id WHERE s.deleted_at IS NULL)::bigint AS sales_count,
    (SELECT count(*) FROM pos.sales s JOIN p ON p.tenant_id=s.tenant_id WHERE s.deleted_at IS NULL AND s.status IN ('completed','returned'))::bigint AS accepted_sales_count,
    (SELECT count(*) FROM pos.payments py JOIN p ON p.tenant_id=py.tenant_id)::bigint AS payment_count,
    (SELECT count(*) FROM pos.payments py JOIN p ON p.tenant_id=py.tenant_id WHERE py.status='approved')::bigint AS approved_payment_count,
    (SELECT count(*) FROM pos.digital_receipts dr JOIN p ON p.tenant_id=dr.tenant_id)::bigint AS receipt_count,
    (SELECT count(*) FROM pos.digital_receipts dr JOIN p ON p.tenant_id=dr.tenant_id WHERE lower(coalesce(dr.status,''))='active')::bigint AS active_receipt_count,
    (SELECT count(*) FROM pos.returns r JOIN p ON p.tenant_id=r.tenant_id)::bigint AS return_count,
    (SELECT count(*) FROM pos.return_refunds rr JOIN p ON p.tenant_id=rr.tenant_id)::bigint AS refund_count,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='open')::bigint AS open_shift_count,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='closed' AND coalesce(cs.difference_cents,0)<>0 AND cs.closed_at>=p.checked_at-interval '24 hours')::bigint AS cash_difference_last_24h_count,
    (SELECT count(*) FROM inventory i JOIN p ON p.tenant_id=i.tenant_id)::bigint AS inventory_item_count,
    (SELECT count(*) FROM inventory i JOIN p ON p.tenant_id=i.tenant_id WHERE i.quantity<0)::bigint AS negative_inventory_item_count,
    (SELECT count(*) FROM pos.inventory_ledger l JOIN p ON p.tenant_id=l.tenant_id)::bigint AS inventory_ledger_entry_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='processed' AND coalesce(i.schema_version,4)=4)::bigint AS processed_schema4_sync_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE coalesce(i.schema_version,4)<>4)::bigint AS legacy_schema_event_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='retry_pending')::bigint AS retry_pending_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='retry_pending' AND i.created_at<p.checked_at-interval '15 minutes')::bigint AS retry_over_sla_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='processing' AND coalesce(i.last_attempt_at,i.created_at)<p.checked_at-interval '15 minutes')::bigint AS stale_processing_count,
    (SELECT count(*) FROM pos.sync_conflicts c JOIN p ON p.tenant_id=c.tenant_id WHERE lower(coalesce(c.status,''))='pending')::bigint AS pending_conflict_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='dead_letter')::bigint AS dead_letter_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='dead_letter' AND coalesce(i.dead_lettered_at,i.created_at)>=p.beta10_at)::bigint AS new_dead_letter_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='dead_letter' AND (i.dead_lettered_at IS NULL OR length(coalesce(i.error_code,''))=0 OR length(coalesce(i.error_message,''))=0))::bigint AS untriaged_dead_letter_count,
    (SELECT count(*) FROM pos.audit_events a JOIN p ON p.tenant_id=a.tenant_id)::bigint AS audit_event_count,
    (SELECT count(*) FROM pos.audit_events a JOIN p ON p.tenant_id=a.tenant_id WHERE a.occurred_at>=p.checked_at-interval '24 hours')::bigint AS audit_events_last_24h,
    (SELECT count(*) FROM pos.update_releases r JOIN p ON p.tenant_id=r.tenant_id WHERE r.channel='beta' AND r.revoked_at IS NULL)::bigint AS active_beta_release_count,
    (SELECT count(*) FROM pos.update_releases r JOIN p ON p.tenant_id=r.tenant_id WHERE r.channel='beta' AND r.revoked_at IS NULL AND (r.package_type<>'velopack' OR r.mandatory=true OR length(coalesce(r.signature,''))<8 OR length(coalesce(r.rollback_version,''))=0))::bigint AS invalid_beta_release_count,
    (SELECT count(*) FROM pos.update_releases r JOIN p ON p.tenant_id=r.tenant_id WHERE r.channel='stable' AND r.revoked_at IS NULL)::bigint AS active_stable_release_count,
    (SELECT count(*) FROM pos.sales s JOIN p ON p.tenant_id=s.tenant_id WHERE s.deleted_at IS NULL AND s.occurred_at>p.beta10_at)::bigint AS sales_after_beta10_count,
    (SELECT count(*) FROM pos.audit_events a JOIN p ON p.tenant_id=a.tenant_id WHERE a.occurred_at>p.beta10_at)::bigint AS audit_events_after_beta10_count
), decision AS (
  SELECT
    array_remove(ARRAY[
      CASE WHEN tenant_count<>1 OR active_tenant_count<>1 THEN 'tenant_baseline_invalid' END,
      CASE WHEN active_store_count<1 THEN 'active_store_baseline_missing' END,
      CASE WHEN active_terminal_count<1 THEN 'active_terminal_baseline_missing' END,
      CASE WHEN active_user_count<1 THEN 'active_user_baseline_missing' END,
      CASE WHEN invalid_modifier_behavior_count>0 OR invalid_substitute_modifier_count>0 THEN 'catalog_modifier_contract_drift' END,
      CASE WHEN legacy_schema_event_count>0 THEN 'schema_version_4_drift_detected' END,
      CASE WHEN stale_processing_count>0 THEN 'stale_sync_processing_detected_after_beta10' END,
      CASE WHEN pending_conflict_count>0 THEN 'pending_sync_conflict_detected_after_beta10' END,
      CASE WHEN new_dead_letter_count>0 THEN 'new_dead_letter_detected_after_beta10' END,
      CASE WHEN untriaged_dead_letter_count>0 THEN 'untriaged_dead_letter_detected' END,
      CASE WHEN open_shift_count>0 THEN 'open_cash_shift_detected_at_ga_baseline' END,
      CASE WHEN cash_difference_last_24h_count>0 THEN 'cash_difference_detected_at_ga_baseline' END,
      CASE WHEN negative_inventory_item_count>0 THEN 'negative_inventory_detected_at_ga_baseline' END,
      CASE WHEN audit_event_count<1 THEN 'audit_baseline_missing' END,
      CASE WHEN active_beta_release_count<1 THEN 'active_beta_release_missing_at_ga_baseline' END,
      CASE WHEN invalid_beta_release_count>0 THEN 'invalid_beta_release_detected_at_ga_baseline' END
    ],NULL) AS blockers,
    array_remove(ARRAY[
      CASE WHEN retry_pending_count>0 THEN 'retry_pending_sync_requires_ga_readiness_closure' END,
      CASE WHEN retry_over_sla_count>0 THEN 'retry_over_sla_requires_ga_readiness_closure' END,
      CASE WHEN dead_letter_count>0 AND new_dead_letter_count=0 AND untriaged_dead_letter_count=0 THEN 'known_dead_letter_triaged_and_stable' END,
      CASE WHEN active_stable_release_count=0 THEN 'stable_channel_promotion_pending' END,
      CASE WHEN sales_after_beta10_count>0 THEN 'commercial_activity_continued_after_beta10_revalidation' END
    ],NULL) AS conditions
  FROM facts
)
SELECT (
  jsonb_build_object(
    'ga01SqlDecision', CASE WHEN cardinality(decision.blockers)=0 THEN 'GO_GA_02' ELSE 'NO_GO_FIX_BLOCKERS' END,
    'checkedAt',p.checked_at,
    'beta10At',p.beta10_at,
    'blockers',decision.blockers,
    'conditions',decision.conditions,
    'tenantCount',tenant_count,
    'activeTenantCount',active_tenant_count,
    'storeCount',store_count,
    'activeStoreCount',active_store_count,
    'terminalCount',terminal_count,
    'activeTerminalCount',active_terminal_count,
    'userCount',user_count,
    'activeUserCount',active_user_count,
    'customerCount',customer_count,
    'productCount',product_count,
    'productPriceCount',product_price_count,
    'modifierCount',modifier_count,
    'invalidModifierBehaviorCount',invalid_modifier_behavior_count,
    'invalidSubstituteModifierCount',invalid_substitute_modifier_count,
    'salesCount',sales_count,
    'acceptedSalesCount',accepted_sales_count,
    'paymentCount',payment_count,
    'approvedPaymentCount',approved_payment_count,
    'receiptCount',receipt_count
  )
  || jsonb_build_object(
    'activeReceiptCount',active_receipt_count,
    'returnCount',return_count,
    'refundCount',refund_count,
    'openShiftCount',open_shift_count,
    'cashDifferenceLast24HoursCount',cash_difference_last_24h_count,
    'inventoryItemCount',inventory_item_count,
    'inventoryLedgerEntryCount',inventory_ledger_entry_count,
    'negativeInventoryItemCount',negative_inventory_item_count,
    'processedSchema4SyncCount',processed_schema4_sync_count,
    'legacySchemaEventCount',legacy_schema_event_count,
    'retryPendingCount',retry_pending_count,
    'retryOverSlaCount',retry_over_sla_count,
    'staleProcessingCount',stale_processing_count,
    'pendingConflictCount',pending_conflict_count,
    'deadLetterCount',dead_letter_count,
    'newDeadLetterCount',new_dead_letter_count,
    'untriagedDeadLetterCount',untriaged_dead_letter_count,
    'auditEventCount',audit_event_count,
    'auditEventsLast24Hours',audit_events_last_24h,
    'activeBetaReleaseCount',active_beta_release_count,
    'invalidBetaReleaseCount',invalid_beta_release_count,
    'activeStableReleaseCount',active_stable_release_count,
    'salesAfterBeta10Count',sales_after_beta10_count,
    'auditEventsAfterBeta10Count',audit_events_after_beta10_count,
    'schemaVersion',4,
    'syncContract','schema_version_4',
    'generalAvailabilityActivated',false
  )
)::text
FROM facts CROSS JOIN decision CROSS JOIN p;
