\set ON_ERROR_STOP on
SELECT set_config('app.tenant_id', :'tenant_id', false);

WITH p AS (
  SELECT :'tenant_id'::uuid AS tenant_id, now() AS checked_at, :'baseline_at'::timestamptz AS baseline_at
), facts AS (
  SELECT
    (SELECT count(*) FROM pos.tenants t JOIN p ON p.tenant_id=t.id WHERE t.status='active')::bigint AS beta_customer_tenant_count,
    (SELECT count(*) FROM pos.customers c JOIN p ON p.tenant_id=c.tenant_id WHERE c.deleted_at IS NULL)::bigint AS pos_customer_count,
    (SELECT count(*) FROM pos.stores s JOIN p ON p.tenant_id=s.tenant_id WHERE s.deleted_at IS NULL)::bigint AS store_count,
    (SELECT count(*) FROM pos.stores s JOIN p ON p.tenant_id=s.tenant_id WHERE s.status='active' AND s.deleted_at IS NULL)::bigint AS active_store_count,
    (SELECT count(*) FROM pos.terminals t JOIN p ON p.tenant_id=t.tenant_id WHERE t.deleted_at IS NULL)::bigint AS terminal_count,
    (SELECT count(*) FROM pos.terminals t JOIN p ON p.tenant_id=t.tenant_id WHERE t.status='active' AND t.deleted_at IS NULL)::bigint AS active_terminal_count,
    (SELECT count(*) FROM pos.sales s JOIN p ON p.tenant_id=s.tenant_id WHERE s.deleted_at IS NULL)::bigint AS sales_count,
    (SELECT count(*) FROM pos.sales s JOIN p ON p.tenant_id=s.tenant_id WHERE s.deleted_at IS NULL AND s.status IN ('completed','returned'))::bigint AS accepted_sales_count,
    (SELECT count(*) FROM pos.payments pm JOIN p ON p.tenant_id=pm.tenant_id)::bigint AS payment_count,
    (SELECT count(*) FROM pos.payments pm JOIN p ON p.tenant_id=pm.tenant_id WHERE pm.status='approved')::bigint AS approved_payment_count,
    (SELECT count(*) FROM pos.payments pm JOIN p ON p.tenant_id=pm.tenant_id WHERE pm.status='declined' AND pm.created_at>=p.checked_at-interval '24 hours')::bigint AS failed_payments_last_24h,
    (SELECT count(*) FROM pos.audit_events a JOIN p ON p.tenant_id=a.tenant_id WHERE a.occurred_at>=p.checked_at-interval '30 days' AND (lower(a.action) LIKE '%incident%' OR lower(a.action) LIKE '%support%'))::bigint AS support_incident_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='retry_pending')::bigint AS retry_pending_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='retry_pending' AND i.created_at<p.checked_at-interval '15 minutes')::bigint AS retry_over_sla_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='processing' AND coalesce(i.last_attempt_at,i.created_at)<p.checked_at-interval '15 minutes')::bigint AS stale_processing_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='processed' AND coalesce(i.schema_version,4)=4)::bigint AS processed_schema4_sync_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE coalesce(i.schema_version,4)<>4)::bigint AS legacy_schema_event_count,
    (SELECT count(*) FROM pos.sync_conflicts c JOIN p ON p.tenant_id=c.tenant_id WHERE lower(coalesce(c.status,''))='pending')::bigint AS pending_conflict_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='dead_letter')::bigint AS dead_letter_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='dead_letter' AND coalesce(i.dead_lettered_at,i.created_at)>=p.baseline_at)::bigint AS new_dead_letter_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='dead_letter' AND (i.dead_lettered_at IS NULL OR length(coalesce(i.error_code,''))=0 OR length(coalesce(i.error_message,''))=0))::bigint AS untriaged_dead_letter_count,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='open')::bigint AS open_shift_count,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='closed' AND coalesce(cs.difference_cents,0)<>0 AND cs.closed_at>=p.checked_at-interval '24 hours')::bigint AS cash_difference_last_24h_count,
    (SELECT count(*) FROM pos.audit_events a JOIN p ON p.tenant_id=a.tenant_id WHERE a.occurred_at>=p.checked_at-interval '24 hours')::bigint AS audit_events_last_24h,
    (SELECT count(*) FROM pos.update_releases r JOIN p ON p.tenant_id=r.tenant_id WHERE r.channel='beta' AND r.revoked_at IS NULL AND r.package_type='velopack' AND r.mandatory=false AND length(coalesce(r.signature,''))>=8 AND length(coalesce(r.rollback_version,''))>0)::bigint AS active_beta_release_count,
    (SELECT count(*) FROM pos.update_releases r JOIN p ON p.tenant_id=r.tenant_id WHERE r.channel='beta' AND r.revoked_at IS NULL AND (r.package_type<>'velopack' OR r.mandatory=true OR length(coalesce(r.signature,''))<8 OR length(coalesce(r.rollback_version,''))=0))::bigint AS invalid_beta_release_count,
    (SELECT count(*) FROM pos.update_releases r JOIN p ON p.tenant_id=r.tenant_id WHERE r.channel='stable' AND r.revoked_at IS NULL)::bigint AS active_stable_release_count
), decision AS (
 SELECT
   array_remove(ARRAY[
     CASE WHEN beta_customer_tenant_count<>1 THEN 'validated_beta_customer_tenant_missing_or_inactive' END,
     CASE WHEN active_store_count<1 THEN 'active_store_missing' END,
     CASE WHEN active_terminal_count<1 THEN 'active_terminal_missing' END,
     CASE WHEN accepted_sales_count<1 OR approved_payment_count<1 THEN 'commercial_activity_evidence_missing' END,
     CASE WHEN failed_payments_last_24h>0 THEN 'failed_payment_requires_review_before_closure' END,
     CASE WHEN stale_processing_count>0 THEN 'stale_sync_processing_blocks_beta_closure' END,
     CASE WHEN pending_conflict_count>0 THEN 'pending_sync_conflict_blocks_beta_closure' END,
     CASE WHEN legacy_schema_event_count>0 THEN 'legacy_sync_schema_event_detected' END,
     CASE WHEN new_dead_letter_count>0 THEN 'new_dead_letter_requires_triage_before_closure' END,
     CASE WHEN untriaged_dead_letter_count>0 THEN 'dead_letter_diagnostic_evidence_incomplete' END,
     CASE WHEN open_shift_count>0 THEN 'open_cash_shift_requires_review_before_closure' END,
     CASE WHEN cash_difference_last_24h_count>0 THEN 'cash_difference_requires_review_before_closure' END,
     CASE WHEN audit_events_last_24h<1 THEN 'audit_evidence_missing' END,
     CASE WHEN active_beta_release_count<1 THEN 'beta_release_readiness_missing' END,
     CASE WHEN invalid_beta_release_count>0 THEN 'invalid_beta_release_detected' END
   ],NULL) AS blockers,
   array_remove(ARRAY[
     CASE WHEN retry_pending_count>0 THEN 'retry_pending_sync_requires_ga_readiness_closure' END,
     CASE WHEN retry_over_sla_count>0 THEN 'retry_over_sla_requires_ga_readiness_closure' END,
     CASE WHEN dead_letter_count>0 AND new_dead_letter_count=0 AND untriaged_dead_letter_count=0 THEN 'known_dead_letter_triaged_and_stable' END,
     CASE WHEN active_stable_release_count=0 THEN 'stable_channel_promotion_pending' END,
     CASE WHEN pos_customer_count=0 THEN 'pos_customer_dataset_empty_review' END
   ],NULL) AS conditions
 FROM facts
)
SELECT json_build_object(
 'beta10SqlDecision', CASE WHEN cardinality(decision.blockers)=0 THEN 'GO_GENERAL_AVAILABILITY_PREP' ELSE 'NO_GO_FIX_BLOCKERS' END,
 'blockers',decision.blockers,
 'conditions',decision.conditions,
 'betaCustomerTenantCount',beta_customer_tenant_count,
 'posCustomerCount',pos_customer_count,
 'storeCount',store_count,
 'activeStoreCount',active_store_count,
 'terminalCount',terminal_count,
 'activeTerminalCount',active_terminal_count,
 'salesCount',sales_count,
 'acceptedSalesCount',accepted_sales_count,
 'paymentCount',payment_count,
 'approvedPaymentCount',approved_payment_count,
 'failedPaymentsLast24Hours',failed_payments_last_24h,
 'supportIncidentCount',support_incident_count,
 'retryPendingCount',retry_pending_count,
 'retryOverSlaCount',retry_over_sla_count,
 'staleProcessingCount',stale_processing_count,
 'processedSchema4SyncCount',processed_schema4_sync_count,
 'legacySchemaEventCount',legacy_schema_event_count,
 'pendingConflictCount',pending_conflict_count,
 'deadLetterCount',dead_letter_count,
 'newDeadLetterCount',new_dead_letter_count,
 'untriagedDeadLetterCount',untriaged_dead_letter_count,
 'openShiftCount',open_shift_count,
 'cashDifferenceLast24HoursCount',cash_difference_last_24h_count,
 'auditEventsLast24Hours',audit_events_last_24h,
 'activeBetaReleaseCount',active_beta_release_count,
 'invalidBetaReleaseCount',invalid_beta_release_count,
 'activeStableReleaseCount',active_stable_release_count,
 'slaPerformance', CASE WHEN stale_processing_count=0 AND pending_conflict_count=0 AND retry_over_sla_count=0 THEN 'PASS' WHEN stale_processing_count=0 AND pending_conflict_count=0 THEN 'PASS_WITH_KNOWN_RETRY_CONDITION' ELSE 'FAIL' END,
 'syncReliability', CASE WHEN stale_processing_count=0 AND pending_conflict_count=0 AND legacy_schema_event_count=0 AND new_dead_letter_count=0 AND untriaged_dead_letter_count=0 THEN 'PASS' ELSE 'FAIL' END,
 'releaseReadiness', CASE WHEN active_beta_release_count>=1 AND invalid_beta_release_count=0 THEN 'PASS' ELSE 'FAIL' END,
 'baselineAt',p.baseline_at,
 'schemaVersion',4,
 'syncContract','schema_version_4'
)::text
FROM facts CROSS JOIN decision CROSS JOIN p;
