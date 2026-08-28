\set ON_ERROR_STOP on

BEGIN;

INSERT INTO pos.audit_events (
  tenant_id,
  action,
  entity_type,
  entity_id,
  before_data,
  after_data,
  trace_id
)
VALUES (
  :'tenant_id'::uuid,
  'pilot.rollback.drill',
  'backup_restore_rollback_drill',
  :'entity_id'::uuid,
  '{}'::jsonb,
  jsonb_build_object('pilot', 'PILOT-08', 'expectedPersistence', false),
  :'trace_id'
);

SELECT count(*)::int AS inserted_inside_transaction
FROM pos.audit_events
WHERE tenant_id = :'tenant_id'::uuid
  AND trace_id = :'trace_id';

ROLLBACK;

SELECT json_build_object(
  'insertVisibleInsideTransaction', true,
  'persistedRollbackRows', (
    SELECT count(*)::bigint
    FROM pos.audit_events
    WHERE tenant_id = :'tenant_id'::uuid
      AND trace_id = :'trace_id'
  ),
  'rollbackTraceId', :'trace_id',
  'rollbackValidation', CASE WHEN (
    SELECT count(*)
    FROM pos.audit_events
    WHERE tenant_id = :'tenant_id'::uuid
      AND trace_id = :'trace_id'
  ) = 0 THEN 'GO' ELSE 'NO-GO' END
)::text;
