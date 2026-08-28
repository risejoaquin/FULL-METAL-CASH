\set ON_ERROR_STOP on
WITH p AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         :'ga02_at'::timestamptz AS ga02_at,
         now() AS checked_at
), topology AS (
  SELECT
    EXISTS (SELECT 1 FROM pos.tenants t JOIN p ON p.tenant_id=t.id WHERE t.status='active' AND t.deleted_at IS NULL) AS tenant_active,
    (SELECT count(*) FROM pos.stores s JOIN p ON p.tenant_id=s.tenant_id WHERE s.status='active' AND s.deleted_at IS NULL)::bigint AS active_store_count,
    (SELECT count(*) FROM pos.terminals t JOIN p ON p.tenant_id=t.tenant_id WHERE t.status='active' AND t.deleted_at IS NULL)::bigint AS active_terminal_count,
    (SELECT count(*) FROM pos.users u JOIN p ON p.tenant_id=u.tenant_id WHERE u.status='active' AND u.deleted_at IS NULL)::bigint AS active_user_count
), sync_state AS (
  SELECT
    count(*) FILTER (WHERE i.status='processed' AND coalesce(i.schema_version,4)=4)::bigint AS processed_schema4_count,
    count(*) FILTER (WHERE i.status='retry_pending')::bigint AS retry_pending_count,
    count(*) FILTER (WHERE i.status='retry_pending' AND i.created_at < p.checked_at-interval '15 minutes')::bigint AS retry_over_sla_count,
    count(*) FILTER (WHERE i.status='processing' AND coalesce(i.last_attempt_at,i.created_at) < p.checked_at-interval '15 minutes')::bigint AS stale_processing_count,
    count(*) FILTER (WHERE i.status='dead_letter')::bigint AS dead_letter_count,
    count(*) FILTER (WHERE i.status='dead_letter' AND coalesce(i.dead_lettered_at,i.created_at) >= p.ga02_at)::bigint AS new_dead_letter_since_ga02_count,
    count(*) FILTER (WHERE coalesce(i.schema_version,4)<4)::bigint AS legacy_schema_event_count
  FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id
), conflicts AS (
  SELECT count(*) FILTER (WHERE lower(coalesce(c.status,''))='pending')::bigint AS pending_conflict_count
  FROM pos.sync_conflicts c JOIN p ON p.tenant_id=c.tenant_id
), operations AS (
  SELECT
    (SELECT count(*) FROM pos.payments py JOIN p ON p.tenant_id=py.tenant_id WHERE lower(coalesce(py.status,''))='failed' AND py.created_at>=p.checked_at-interval '24 hours')::bigint AS failed_payments_24h,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='open')::bigint AS open_shift_count,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN p ON p.tenant_id=cs.tenant_id WHERE cs.closed_at>=p.checked_at-interval '24 hours' AND coalesce(cs.difference_cents,0)<>0)::bigint AS cash_difference_24h
), inventory AS (
  SELECT count(*) FILTER (WHERE q.quantity < 0)::bigint AS negative_inventory_item_count
  FROM (
    SELECT il.store_id,il.product_id,sum(il.quantity_delta) AS quantity
    FROM pos.inventory_ledger il JOIN p ON p.tenant_id=il.tenant_id
    GROUP BY il.store_id,il.product_id
  ) q
), audit_state AS (
  SELECT
    count(*)::bigint AS audit_event_count,
    count(*) FILTER (WHERE a.occurred_at>=p.checked_at-interval '24 hours')::bigint AS audit_events_24h,
    count(*) FILTER (WHERE a.action='ga02.sync_retry_closed_as_historical_validation')::bigint AS ga02_retry_closure_audit_count,
    count(*) FILTER (WHERE a.action='ga02.dead_letter_closed_as_historical_evidence')::bigint AS ga02_dead_letter_decision_audit_count,
    count(*) FILTER (WHERE a.occurred_at>=p.ga02_at AND (lower(a.action) LIKE '%incident%' OR lower(a.action) LIKE '%support%'))::bigint AS support_incident_audit_since_ga02_count
  FROM pos.audit_events a JOIN p ON p.tenant_id=a.tenant_id
), blockers AS (
 SELECT array_remove(ARRAY[
   CASE WHEN NOT t.tenant_active THEN 'tenant_inactive' END,
   CASE WHEN t.active_store_count<1 THEN 'active_store_missing' END,
   CASE WHEN t.active_terminal_count<1 THEN 'active_terminal_missing' END,
   CASE WHEN t.active_user_count<1 THEN 'active_user_missing' END,
   CASE WHEN s.retry_pending_count>0 THEN 'retry_pending_requires_incident_route' END,
   CASE WHEN s.retry_over_sla_count>0 THEN 'retry_over_sla_requires_incident_route' END,
   CASE WHEN s.stale_processing_count>0 THEN 'stale_processing_requires_incident_route' END,
   CASE WHEN c.pending_conflict_count>0 THEN 'pending_conflict_requires_incident_route' END,
   CASE WHEN s.new_dead_letter_since_ga02_count>0 THEN 'new_dead_letter_requires_incident_route' END,
   CASE WHEN s.legacy_schema_event_count>0 THEN 'legacy_schema_event' END,
   CASE WHEN o.failed_payments_24h>0 THEN 'failed_payment_last_24h' END,
   CASE WHEN o.cash_difference_24h>0 THEN 'cash_difference_last_24h' END,
   CASE WHEN i.negative_inventory_item_count>0 THEN 'negative_inventory' END,
   CASE WHEN a.audit_events_24h<1 THEN 'audit_evidence_last_24h_missing' END,
   CASE WHEN s.dead_letter_count>0 AND a.ga02_dead_letter_decision_audit_count<1 THEN 'historical_dead_letter_decision_evidence_missing' END
 ],NULL) AS blocker_list
 FROM topology t CROSS JOIN sync_state s CROSS JOIN conflicts c CROSS JOIN operations o CROSS JOIN inventory i CROSS JOIN audit_state a
)
SELECT jsonb_build_object(
 'ga03SqlContract','ga_support_incident_slo_operations_readiness',
 'checkedAt',p.checked_at,
 'ga02At',p.ga02_at,
 'tenantActive',t.tenant_active,
 'activeStoreCount',t.active_store_count,
 'activeTerminalCount',t.active_terminal_count,
 'activeUserCount',t.active_user_count,
 'processedSchema4SyncCount',s.processed_schema4_count,
 'retryPendingCount',s.retry_pending_count,
 'retryOverSlaCount',s.retry_over_sla_count,
 'staleProcessingCount',s.stale_processing_count,
 'pendingConflictCount',c.pending_conflict_count,
 'deadLetterCount',s.dead_letter_count,
 'newDeadLetterSinceGa02Count',s.new_dead_letter_since_ga02_count,
 'legacySchemaEventCount',s.legacy_schema_event_count,
 'failedPaymentsLast24Hours',o.failed_payments_24h,
 'openShiftCount',o.open_shift_count,
 'cashDifferenceLast24HoursCount',o.cash_difference_24h,
 'negativeInventoryItemCount',i.negative_inventory_item_count,
 'auditEventCount',a.audit_event_count,
 'auditEventsLast24Hours',a.audit_events_24h,
 'ga02RetryClosureAuditCount',a.ga02_retry_closure_audit_count,
 'ga02DeadLetterDecisionAuditCount',a.ga02_dead_letter_decision_audit_count,
 'supportIncidentAuditSinceGa02Count',a.support_incident_audit_since_ga02_count,
 'blockers',coalesce(b.blocker_list,ARRAY[]::text[]),
 'schemaVersion',4,
 'syncContract','schema_version_4',
 'generalAvailabilityActivated',false
)::text
FROM p CROSS JOIN topology t CROSS JOIN sync_state s CROSS JOIN conflicts c CROSS JOIN operations o CROSS JOIN inventory i CROSS JOIN audit_state a CROSS JOIN blockers b;
