\set ON_ERROR_STOP on
WITH p AS (
 SELECT :'tenant_id'::uuid AS tenant_id, :'ga01_at'::timestamptz AS ga01_at, now() AS changed_at
), retry_candidates AS (
 SELECT i.id,i.tenant_id,i.terminal_id,i.event_id,i.event_type,i.entity_type,i.status,i.attempts,i.max_attempts,
        i.error_code,i.error_message,i.created_at,i.next_retry_at,t.fingerprint,i.payload
 FROM pos.sync_inbox_events i
 JOIN p ON p.tenant_id=i.tenant_id
 LEFT JOIN pos.terminals t ON t.tenant_id=i.tenant_id AND t.id=i.terminal_id
 WHERE i.status='retry_pending'
   AND i.created_at < now()-interval '15 minutes'
   AND (
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
        AND i.created_at < p.ga01_at
        AND i.error_code='processing_exception'
        AND coalesce(i.error_message,'') LIKE 'The JSON value could not be converted to SolidPOS.PosServer.Contracts.Sales.CreateSaleLineRequest.%'
      )
   )
), updated AS (
 UPDATE pos.sync_inbox_events i
 SET status='rejected',
     result=coalesce(i.result,'{}'::jsonb) || jsonb_build_object('ga02Closure','historical_validation_fixture','closedAt',now()),
     error_code='ga02_historical_validation_fixture_closed',
     error_message='GA-02 closed an over-SLA validation fixture as non-commercial historical evidence; original event remains in sync inbox history.',
     next_retry_at=NULL,
     processed_at=coalesce(i.processed_at,now())
 FROM retry_candidates c
 WHERE i.tenant_id=c.tenant_id AND i.id=c.id AND i.status='retry_pending'
 RETURNING i.id,i.tenant_id,i.terminal_id,i.event_id,i.event_type,i.entity_type,i.status,i.processed_at,c.error_code AS before_error_code,c.error_message AS before_error_message,c.created_at,c.fingerprint,c.payload
), retry_audit AS (
 INSERT INTO pos.audit_events(tenant_id,terminal_id,action,entity_type,entity_id,before_data,after_data,trace_id,occurred_at)
 SELECT u.tenant_id,u.terminal_id,'ga02.sync_retry_closed_as_historical_validation','sync_inbox_event',u.id,
   jsonb_build_object('status','retry_pending','errorCode',u.before_error_code,'errorMessage',u.before_error_message,'createdAt',u.created_at,'fingerprint',u.fingerprint,'payloadSource',u.payload->>'source'),
   jsonb_build_object('status','rejected','decision','close_as_historical_evidence','reason','over-SLA controlled validation fixture; not executable commercial work','processedAt',u.processed_at),
   'ga-02-sync-queue-sla-closure',now()
 FROM updated u
 RETURNING id
), historical_dl AS (
 SELECT i.id,i.tenant_id,i.terminal_id,i.event_id,i.event_type,i.entity_type,i.created_at,i.dead_lettered_at,i.error_code,i.error_message,i.replayed_at,i.replay_reason,t.fingerprint,i.payload
 FROM pos.sync_inbox_events i
 JOIN p ON p.tenant_id=i.tenant_id
 LEFT JOIN pos.terminals t ON t.tenant_id=i.tenant_id AND t.id=i.terminal_id
 WHERE i.status='dead_letter'
   AND coalesce(i.dead_lettered_at,i.created_at)<p.ga01_at
   AND i.dead_lettered_at IS NOT NULL AND length(coalesce(i.error_code,''))>0 AND length(coalesce(i.error_message,''))>0
   AND (t.fingerprint LIKE 'pilot-%' OR t.fingerprint LIKE 'exp-%' OR t.fingerprint LIKE 'iteration-%' OR t.fingerprint LIKE 'beta-%' OR t.fingerprint LIKE 'ga-%')
   AND (
      i.entity_type IN ('sync_recovery_probe','sync_dead_letter_probe')
      OR i.event_type LIKE 'pilot.%' OR i.event_type LIKE 'beta.%' OR i.event_type LIKE 'exp.%' OR i.event_type LIKE 'iteration.%' OR i.event_type LIKE 'ga.%'
      OR coalesce(i.payload->>'source','') ~* '^(pilot|beta|exp|iteration|ga)-'
      OR coalesce(i.error_message,'') ~* '(controlled|validation|probe)'
   )
), dl_audit AS (
 INSERT INTO pos.audit_events(tenant_id,terminal_id,action,entity_type,entity_id,before_data,after_data,trace_id,occurred_at)
 SELECT d.tenant_id,d.terminal_id,'ga02.dead_letter_closed_as_historical_evidence','sync_inbox_event',d.id,
   jsonb_build_object('status','dead_letter','eventType',d.event_type,'entityType',d.entity_type,'errorCode',d.error_code,'errorMessage',d.error_message,'deadLetteredAt',d.dead_lettered_at,'replayedAt',d.replayed_at,'replayReason',d.replay_reason,'fingerprint',d.fingerprint,'payloadSource',d.payload->>'source'),
   jsonb_build_object('status','dead_letter','decision','close_as_historical_evidence','executableWorkPending',false,'reason','triaged controlled validation dead-letter retained immutably as historical evidence'),
   'ga-02-sync-queue-sla-closure',now()
 FROM historical_dl d
 WHERE NOT EXISTS (
   SELECT 1 FROM pos.audit_events a WHERE a.tenant_id=d.tenant_id AND a.entity_type='sync_inbox_event' AND a.entity_id=d.id
     AND a.action='ga02.dead_letter_closed_as_historical_evidence'
 )
 RETURNING id
)
SELECT json_build_object(
 'closedValidationRetryCount',(SELECT count(*) FROM updated),
 'retryAuditEventCount',(SELECT count(*) FROM retry_audit),
 'historicalDeadLetterDecisionCount',(SELECT count(*) FROM historical_dl),
 'newDeadLetterAuditEventCount',(SELECT count(*) FROM dl_audit),
 'decision','SAFE_VALIDATION_FIXTURE_CLOSURE_APPLIED'
)::text;
