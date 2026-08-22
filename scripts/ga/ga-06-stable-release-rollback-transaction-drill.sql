\set ON_ERROR_STOP on
BEGIN;
SELECT set_config('app.tenant_id', :'tenant_id', true);
UPDATE pos.update_releases SET revoked_at=now()
WHERE id=:'stable_release_id'::uuid AND tenant_id=:'tenant_id'::uuid AND channel='stable' AND revoked_at IS NULL;
WITH during AS (
 SELECT count(*)::bigint active_count FROM pos.update_releases WHERE id=:'stable_release_id'::uuid AND tenant_id=:'tenant_id'::uuid AND revoked_at IS NULL
), target AS (
 SELECT count(*)::bigint rollback_count FROM pos.update_releases WHERE tenant_id=:'tenant_id'::uuid AND channel='beta' AND version=:'rollback_version' AND revoked_at IS NULL
)
SELECT json_build_object('stableInactiveDuringDrill',(SELECT active_count FROM during)=0,'rollbackTargetAvailable',(SELECT rollback_count FROM target)>=1,'rollbackValidation',CASE WHEN (SELECT active_count FROM during)=0 AND (SELECT rollback_count FROM target)>=1 THEN 'GO' ELSE 'NO-GO' END)::text;
ROLLBACK;
