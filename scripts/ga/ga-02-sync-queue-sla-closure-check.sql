\set ON_ERROR_STOP on
WITH p AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         :'ga01_at'::timestamptz AS ga01_at,
         now() AS checked_at,
         interval '15 minutes' AS retry_sla
), inbox AS (
  SELECT i.*, t.fingerprint,
         (
           (
             (t.fingerprint LIKE 'pilot-%' OR t.fingerprint LIKE 'exp-%' OR t.fingerprint LIKE 'iteration-%' OR t.fingerprint LIKE 'beta-%' OR t.fingerprint LIKE 'ga-%')
             AND (
               i.entity_type IN ('sync_recovery_probe','sync_dead_letter_probe')
               OR i.event_type LIKE 'pilot.%' OR i.event_type LIKE 'beta.%' OR i.event_type LIKE 'exp.%' OR i.event_type LIKE 'iteration.%' OR i.event_type LIKE 'ga.%'
               OR coalesce(i.payload->>'source','') ~* '^(pilot|beta|exp|iteration|ga)-'
               OR coalesce(i.error_message,'') ~* '(controlled|validation|probe)'
             )
           )
           OR (
             t.fingerprint = ('iteration-05-poscore-sync-' || i.tenant_id::text)
             AND i.event_type='sale.completed'
             AND i.entity_type='sale'
             AND i.status='retry_pending'
             AND i.created_at < p.ga01_at
             AND i.error_code='processing_exception'
             AND coalesce(i.error_message,'') LIKE 'The JSON value could not be converted to SolidPOS.PosServer.Contracts.Sales.CreateSaleLineRequest.%'
           )
         ) AS validation_fixture
  FROM pos.sync_inbox_events i
  JOIN p ON p.tenant_id=i.tenant_id
  LEFT JOIN pos.terminals t ON t.tenant_id=i.tenant_id AND t.id=i.terminal_id
), counts AS (
 SELECT
   count(*) FILTER (WHERE status='processed' AND coalesce(schema_version,4)=4)::bigint AS processed_schema4_count,
   count(*) FILTER (WHERE coalesce(schema_version,4)<4)::bigint AS legacy_schema_event_count,
   count(*) FILTER (WHERE status='retry_pending')::bigint AS retry_pending_count,
   count(*) FILTER (WHERE status='retry_pending' AND coalesce(next_retry_at,created_at)<=p.checked_at)::bigint AS retry_due_count,
   count(*) FILTER (WHERE status='retry_pending' AND created_at<p.checked_at-p.retry_sla)::bigint AS retry_over_sla_count,
   count(*) FILTER (WHERE status='retry_pending' AND validation_fixture)::bigint AS validation_retry_count,
   count(*) FILTER (WHERE status='retry_pending' AND NOT coalesce(validation_fixture,false))::bigint AS ambiguous_retry_count,
   count(*) FILTER (WHERE status='processing' AND coalesce(last_attempt_at,created_at)<p.checked_at-p.retry_sla)::bigint AS stale_processing_count,
   count(*) FILTER (WHERE status='dead_letter')::bigint AS dead_letter_count,
   count(*) FILTER (WHERE status='dead_letter' AND coalesce(dead_lettered_at,created_at)>=p.ga01_at)::bigint AS new_dead_letter_count,
   count(*) FILTER (WHERE status='dead_letter' AND (dead_lettered_at IS NULL OR length(coalesce(error_code,''))=0 OR length(coalesce(error_message,''))=0))::bigint AS untriaged_dead_letter_count,
   count(*) FILTER (WHERE status='dead_letter' AND validation_fixture AND coalesce(dead_lettered_at,created_at)<p.ga01_at AND dead_lettered_at IS NOT NULL AND length(coalesce(error_code,''))>0 AND length(coalesce(error_message,''))>0)::bigint AS historical_validation_dead_letter_count,
   count(*) FILTER (WHERE status='dead_letter' AND NOT (coalesce(validation_fixture,false) AND coalesce(dead_lettered_at,created_at)<p.ga01_at AND dead_lettered_at IS NOT NULL AND length(coalesce(error_code,''))>0 AND length(coalesce(error_message,''))>0))::bigint AS actionable_or_ambiguous_dead_letter_count
 FROM inbox CROSS JOIN p
), conflict_counts AS (
 SELECT count(*) FILTER (WHERE lower(coalesce(status,''))='pending')::bigint AS pending_conflict_count
 FROM pos.sync_conflicts c JOIN p ON p.tenant_id=c.tenant_id
), dup_batch AS (
 SELECT count(*)::bigint AS duplicate_batch_sequence_violation_count FROM (
  SELECT i.tenant_id,i.terminal_id,i.batch_id,i.sequence_number FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id
  WHERE i.batch_id IS NOT NULL AND i.sequence_number IS NOT NULL
  GROUP BY i.tenant_id,i.terminal_id,i.batch_id,i.sequence_number HAVING count(*)>1
 ) x
), dup_event AS (
 SELECT count(*)::bigint AS duplicate_event_identity_count FROM (
  SELECT i.tenant_id,i.terminal_id,i.event_id FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id
  GROUP BY i.tenant_id,i.terminal_id,i.event_id HAVING count(*)>1
 ) x
), audit_closure AS (
 SELECT
   count(*) FILTER (WHERE a.action='ga02.dead_letter_closed_as_historical_evidence')::bigint AS historical_dead_letter_audit_count,
   count(*) FILTER (WHERE a.action='ga02.sync_retry_closed_as_historical_validation')::bigint AS retry_closure_audit_count
 FROM pos.audit_events a JOIN p ON p.tenant_id=a.tenant_id
), details AS (
 SELECT
   coalesce((SELECT jsonb_agg(jsonb_build_object(
      'id',id,'terminalId',terminal_id,'eventId',event_id,'eventType',event_type,'entityType',entity_type,
      'status',status,'attempts',attempts,'maxAttempts',max_attempts,'createdAt',created_at,'nextRetryAt',next_retry_at,
      'errorCode',error_code,'errorMessage',error_message,'validationFixture',validation_fixture,'terminalFingerprint',fingerprint,
      'payloadSource',payload->>'source') ORDER BY created_at)
     FROM inbox WHERE status='retry_pending'),'[]'::jsonb) AS retry_details,
   coalesce((SELECT jsonb_agg(jsonb_build_object(
      'id',id,'terminalId',terminal_id,'eventId',event_id,'eventType',event_type,'entityType',entity_type,
      'status',status,'attempts',attempts,'maxAttempts',max_attempts,'createdAt',created_at,'deadLetteredAt',dead_lettered_at,
      'errorCode',error_code,'errorMessage',error_message,'replayedAt',replayed_at,'replayReason',replay_reason,
      'validationFixture',validation_fixture,'terminalFingerprint',fingerprint,'payloadSource',payload->>'source') ORDER BY created_at)
     FROM inbox WHERE status='dead_letter'),'[]'::jsonb) AS dead_letter_details
), blockers AS (
 SELECT array_remove(ARRAY[
   CASE WHEN c.legacy_schema_event_count>0 THEN 'legacy_schema_event' END,
   CASE WHEN c.retry_pending_count>0 AND c.ambiguous_retry_count>0 THEN 'retry_without_safe_ga02_decision' END,
   CASE WHEN c.stale_processing_count>0 THEN 'stale_processing' END,
   CASE WHEN cc.pending_conflict_count>0 THEN 'pending_conflict' END,
   CASE WHEN c.new_dead_letter_count>0 THEN 'new_dead_letter' END,
   CASE WHEN c.untriaged_dead_letter_count>0 THEN 'untriaged_dead_letter' END,
   CASE WHEN c.actionable_or_ambiguous_dead_letter_count>0 THEN 'dead_letter_requires_explicit_decision' END,
   CASE WHEN db.duplicate_batch_sequence_violation_count>0 OR de.duplicate_event_identity_count>0 THEN 'idempotency_violation' END
 ],NULL) AS blocker_list
 FROM counts c CROSS JOIN conflict_counts cc CROSS JOIN dup_batch db CROSS JOIN dup_event de
)
SELECT (
 jsonb_build_object(
  'ga02SqlContract','ga_sync_queue_sla_closure','checkedAt',p.checked_at,'ga01At',p.ga01_at,
  'processedSchema4SyncCount',c.processed_schema4_count,'legacySchemaEventCount',c.legacy_schema_event_count,
  'retryPendingCount',c.retry_pending_count,'retryDueCount',c.retry_due_count,'retryOverSlaCount',c.retry_over_sla_count,
  'validationRetryCount',c.validation_retry_count,'ambiguousRetryCount',c.ambiguous_retry_count,
  'staleProcessingCount',c.stale_processing_count,'pendingConflictCount',cc.pending_conflict_count,
  'deadLetterCount',c.dead_letter_count,'newDeadLetterCount',c.new_dead_letter_count,
  'untriagedDeadLetterCount',c.untriaged_dead_letter_count,'historicalValidationDeadLetterCount',c.historical_validation_dead_letter_count,
  'actionableOrAmbiguousDeadLetterCount',c.actionable_or_ambiguous_dead_letter_count,
  'duplicateBatchSequenceViolationCount',db.duplicate_batch_sequence_violation_count,
  'duplicateEventIdentityCount',de.duplicate_event_identity_count,
  'historicalDeadLetterAuditCount',ac.historical_dead_letter_audit_count,'retryClosureAuditCount',ac.retry_closure_audit_count,
  'schemaVersion',4,'syncContract','schema_version_4'
 ) || jsonb_build_object(
  'retryDetails',d.retry_details,'deadLetterDetails',d.dead_letter_details,
  'blockers',coalesce(b.blocker_list,ARRAY[]::text[]),
  'canAutoCloseValidationRetries',(c.retry_pending_count>0 AND c.retry_pending_count=c.validation_retry_count AND c.retry_over_sla_count>0),
  'historicalDeadLetterDecision',CASE
    WHEN c.dead_letter_count=0 THEN 'none'
    WHEN c.actionable_or_ambiguous_dead_letter_count=0 THEN 'close_as_historical_evidence'
    ELSE 'manual_decision_required' END,
  'generalAvailabilityActivated',false
 )
)::text
FROM p CROSS JOIN counts c CROSS JOIN conflict_counts cc CROSS JOIN dup_batch db CROSS JOIN dup_event de CROSS JOIN audit_closure ac CROSS JOIN details d CROSS JOIN blockers b;
