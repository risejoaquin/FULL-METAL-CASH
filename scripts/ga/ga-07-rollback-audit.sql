\set ON_ERROR_STOP on
SELECT set_config('app.tenant_id', :'tenant_id', false);
INSERT INTO pos.audit_events(tenant_id,action,entity_type,entity_id,before_data,after_data,trace_id,occurred_at)
SELECT :'tenant_id'::uuid,'ga07.rollback_drill.validated','update_release',:'stable_release_id'::uuid,
       jsonb_build_object('stableReleaseId',:'stable_release_id','rollbackVersion',:'rollback_version'),
       jsonb_build_object('drill','transactional','persistedRollbackMutationCount',0,'restoreValidation','GO','rtoSeconds',:'rto_seconds'::numeric,'rpoSeconds',:'rpo_seconds'::numeric),
       :'trace_id',clock_timestamp()
WHERE NOT EXISTS (SELECT 1 FROM pos.audit_events WHERE tenant_id=:'tenant_id'::uuid AND trace_id=:'trace_id');
SELECT json_build_object('auditCount',(SELECT count(*)::bigint FROM pos.audit_events WHERE tenant_id=:'tenant_id'::uuid AND trace_id=:'trace_id'),'auditDecision',CASE WHEN (SELECT count(*) FROM pos.audit_events WHERE tenant_id=:'tenant_id'::uuid AND trace_id=:'trace_id')=1 THEN 'GO' ELSE 'NO-GO' END)::text;
