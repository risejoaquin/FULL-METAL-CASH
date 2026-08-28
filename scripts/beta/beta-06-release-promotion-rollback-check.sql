\set ON_ERROR_STOP on
SELECT set_config('app.tenant_id', :'tenant_id', false);

WITH source_release AS (
  SELECT * FROM pos.update_releases
  WHERE id = :'source_release_id'::uuid AND tenant_id = :'tenant_id'::uuid
), promoted_release AS (
  SELECT * FROM pos.update_releases
  WHERE id = :'promoted_release_id'::uuid AND tenant_id = :'tenant_id'::uuid
), facts AS (
  SELECT
    (SELECT count(*) FROM source_release) AS source_release_count,
    (SELECT count(*) FROM promoted_release) AS promoted_release_count,
    (SELECT count(*) FROM source_release WHERE channel='internal' AND revoked_at IS NULL) AS source_internal_active_count,
    (SELECT count(*) FROM promoted_release WHERE channel='beta' AND revoked_at IS NULL) AS promoted_beta_active_count,
    (SELECT count(*) FROM promoted_release WHERE mandatory=false) AS non_mandatory_count,
    (SELECT count(*) FROM promoted_release WHERE universal_installer=true AND package_type='velopack') AS universal_velopack_count,
    (SELECT count(*) FROM promoted_release WHERE length(artifact_hash) >= 32 AND length(signature) >= 8) AS artifact_integrity_count,
    (SELECT count(*) FROM promoted_release WHERE rollback_version IS NOT NULL AND length(rollback_version) > 0) AS rollback_version_count,
    (SELECT count(*) FROM source_release s JOIN promoted_release p ON p.version=s.version AND p.package_type=s.package_type AND p.artifact_url=s.artifact_url AND p.artifact_hash=s.artifact_hash AND p.signature=s.signature) AS promotion_artifact_match_count,
    (SELECT count(*) FROM pos.audit_events WHERE tenant_id=:'tenant_id'::uuid AND action='updates.release.created' AND entity_id::text IN (:'source_release_id', :'promoted_release_id')) AS release_audit_count,
    (SELECT count(*) FROM pos.update_releases WHERE tenant_id=:'tenant_id'::uuid AND version=:'release_version' AND package_type='velopack' AND channel IN ('internal','beta')) AS promoted_pair_count
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN source_release_count <> 1 THEN 'source_internal_release_missing' END,
    CASE WHEN promoted_release_count <> 1 THEN 'promoted_beta_release_missing' END,
    CASE WHEN source_internal_active_count <> 1 THEN 'source_internal_release_not_active' END,
    CASE WHEN promoted_beta_active_count <> 1 THEN 'promoted_beta_release_not_active' END,
    CASE WHEN non_mandatory_count <> 1 THEN 'beta_release_must_be_non_mandatory' END,
    CASE WHEN universal_velopack_count <> 1 THEN 'universal_velopack_installer_missing' END,
    CASE WHEN artifact_integrity_count <> 1 THEN 'artifact_hash_or_signature_missing' END,
    CASE WHEN rollback_version_count <> 1 THEN 'rollback_version_missing' END,
    CASE WHEN promotion_artifact_match_count <> 1 THEN 'promotion_changed_artifact_identity' END,
    CASE WHEN release_audit_count < 2 THEN 'release_creation_audit_evidence_missing' END,
    CASE WHEN promoted_pair_count < 2 THEN 'internal_beta_promotion_pair_missing' END
  ], NULL) AS items FROM facts
)
SELECT json_build_object(
  'sourceReleaseCount', source_release_count,
  'promotedReleaseCount', promoted_release_count,
  'sourceInternalActiveCount', source_internal_active_count,
  'promotedBetaActiveCount', promoted_beta_active_count,
  'nonMandatoryCount', non_mandatory_count,
  'universalVelopackCount', universal_velopack_count,
  'artifactIntegrityCount', artifact_integrity_count,
  'rollbackVersionCount', rollback_version_count,
  'promotionArtifactMatchCount', promotion_artifact_match_count,
  'releaseAuditCount', release_audit_count,
  'promotedPairCount', promoted_pair_count,
  'blockers', blockers.items,
  'schemaVersion', 4,
  'syncContract', 'schema_version_4',
  'beta06SqlDecision', CASE WHEN cardinality(blockers.items)=0 THEN 'GO' ELSE 'NO-GO' END
)::text
FROM facts CROSS JOIN blockers;
