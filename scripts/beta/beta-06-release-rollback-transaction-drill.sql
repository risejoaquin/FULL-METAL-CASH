\set ON_ERROR_STOP on
SELECT set_config('app.tenant_id', :'tenant_id', false);
BEGIN;
UPDATE pos.update_releases
SET revoked_at = now()
WHERE id = :'promoted_release_id'::uuid
  AND tenant_id = :'tenant_id'::uuid
  AND revoked_at IS NULL;
SELECT CASE WHEN EXISTS (
  SELECT 1 FROM pos.update_releases
  WHERE id = :'promoted_release_id'::uuid
    AND tenant_id = :'tenant_id'::uuid
    AND revoked_at IS NOT NULL
) THEN 'GO' ELSE 'NO-GO' END AS rollback_inside_transaction;
ROLLBACK;
SELECT json_build_object(
  'promotedReleaseActiveAfterRollback', EXISTS (
    SELECT 1 FROM pos.update_releases
    WHERE id = :'promoted_release_id'::uuid
      AND tenant_id = :'tenant_id'::uuid
      AND revoked_at IS NULL
  ),
  'rollbackValidation', CASE WHEN EXISTS (
    SELECT 1 FROM pos.update_releases
    WHERE id = :'promoted_release_id'::uuid
      AND tenant_id = :'tenant_id'::uuid
      AND revoked_at IS NULL
  ) THEN 'GO' ELSE 'NO-GO' END,
  'persistedRollbackMutationCount', (
    SELECT count(*) FROM pos.update_releases
    WHERE id = :'promoted_release_id'::uuid
      AND tenant_id = :'tenant_id'::uuid
      AND revoked_at IS NOT NULL
  )
)::text;
