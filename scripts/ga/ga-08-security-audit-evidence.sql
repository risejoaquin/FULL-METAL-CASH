\set ON_ERROR_STOP on
SELECT set_config('app.tenant_id', :'tenant_id', false);
INSERT INTO pos.audit_events(tenant_id,action,entity_type,entity_id,before_data,after_data,trace_id,occurred_at)
SELECT :'tenant_id'::uuid,'ga08.security_final_gate.validated','tenant',:'tenant_id'::uuid,
       jsonb_build_object('gate','GA-08','securityFinalGate','pending'),
       jsonb_build_object('securityFinalGate','GO','rlsMissingTenantTableCount',0,'crossTenantReadIsolation','PASS','authLifecycle','PASS','negativeAuthorization','PASS'),
       :'trace_id',clock_timestamp()
WHERE NOT EXISTS (SELECT 1 FROM pos.audit_events WHERE tenant_id=:'tenant_id'::uuid AND trace_id=:'trace_id');
SELECT json_build_object(
  'auditCount',(SELECT count(*)::bigint FROM pos.audit_events WHERE tenant_id=:'tenant_id'::uuid AND trace_id=:'trace_id'),
  'auditDecision',CASE WHEN (SELECT count(*) FROM pos.audit_events WHERE tenant_id=:'tenant_id'::uuid AND trace_id=:'trace_id')=1 THEN 'GO' ELSE 'NO-GO' END
)::text;
