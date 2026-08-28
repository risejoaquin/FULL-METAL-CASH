\set ON_ERROR_STOP on
BEGIN;
SELECT set_config('app.tenant_id', :'tenant_id', true);
UPDATE pos.update_releases
SET revoked_at=clock_timestamp()
WHERE id=:'stable_release_id'::uuid
  AND tenant_id=:'tenant_id'::uuid
  AND channel='stable'
  AND revoked_at IS NULL;

SELECT
  ((SELECT count(*)::bigint
    FROM pos.update_releases
    WHERE id=:'stable_release_id'::uuid
      AND tenant_id=:'tenant_id'::uuid
      AND revoked_at IS NULL)=0)::int AS stable_inactive_during_drill,
  ((SELECT count(*)::bigint
    FROM pos.update_releases
    WHERE tenant_id=:'tenant_id'::uuid
      AND version=:'rollback_version'
      AND revoked_at IS NULL
      AND universal_installer=true
      AND mandatory=false
      AND artifact_url<>''
      AND artifact_hash<>''
      AND signature<>'')>=1)::int AS rollback_target_available
\gset ga07_

ROLLBACK;

SELECT json_build_object(
 'stableInactiveDuringDrill',(:'ga07_stable_inactive_during_drill'::int=1),
 'rollbackTargetAvailable',(:'ga07_rollback_target_available'::int=1),
 'rollbackValidation',CASE
   WHEN :'ga07_stable_inactive_during_drill'::int=1
    AND :'ga07_rollback_target_available'::int=1
   THEN 'GO' ELSE 'NO-GO' END,
 'stableActiveAfterRollback',(SELECT count(*) FROM pos.update_releases
   WHERE id=:'stable_release_id'::uuid
     AND tenant_id=:'tenant_id'::uuid
     AND revoked_at IS NULL)=1,
 'persistedRollbackMutationCount',(SELECT count(*)::bigint FROM pos.update_releases
   WHERE id=:'stable_release_id'::uuid
     AND tenant_id=:'tenant_id'::uuid
     AND revoked_at IS NOT NULL),
 'postRollbackValidation',CASE WHEN (SELECT count(*) FROM pos.update_releases
   WHERE id=:'stable_release_id'::uuid
     AND tenant_id=:'tenant_id'::uuid
     AND revoked_at IS NULL)=1 THEN 'GO' ELSE 'NO-GO' END
)::text;
