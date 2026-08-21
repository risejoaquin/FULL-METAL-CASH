
\set ON_ERROR_STOP on

WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         :'release_id'::uuid AS release_id,
         :'release_version'::text AS release_version,
         now() AS checked_at
), table_status AS (
  SELECT bool_and(to_regclass(table_name) IS NOT NULL) AS required_tables_present
  FROM (VALUES
    ('pos.tenants'),
    ('pos.update_releases'),
    ('pos.builder_projects'),
    ('pos.builder_builds'),
    ('pos.audit_events'),
    ('pos.sync_changes')
  ) required(table_name)
), topology AS (
  SELECT
    EXISTS (SELECT 1 FROM pos.tenants t JOIN params p ON p.tenant_id = t.id WHERE t.status = 'active' AND t.deleted_at IS NULL) AS tenant_active
), release_row AS (
  SELECT ur.*
  FROM pos.update_releases ur
  JOIN params p ON p.release_id = ur.id AND p.tenant_id = ur.tenant_id AND p.release_version = ur.version
), release_check AS (
  SELECT
    EXISTS (SELECT 1 FROM release_row) AS release_exists,
    coalesce((SELECT channel FROM release_row LIMIT 1),'') AS channel,
    coalesce((SELECT package_type FROM release_row LIMIT 1),'') AS package_type,
    coalesce((SELECT artifact_url FROM release_row LIMIT 1),'') AS artifact_url,
    coalesce((SELECT artifact_hash FROM release_row LIMIT 1),'') AS artifact_hash,
    coalesce((SELECT signature FROM release_row LIMIT 1),'') AS signature,
    coalesce((SELECT rollback_version FROM release_row LIMIT 1),'') AS rollback_version,
    coalesce((SELECT mandatory FROM release_row LIMIT 1), true) AS mandatory,
    coalesce((SELECT universal_installer FROM release_row LIMIT 1), false) AS universal_installer,
    (SELECT revoked_at FROM release_row LIMIT 1) AS revoked_at,
    coalesce((SELECT tenant_id IS NOT NULL FROM release_row LIMIT 1), false) AS tenant_scoped,
    (SELECT published_at FROM release_row LIMIT 1) AS published_at
), release_inventory AS (
  SELECT
    count(*) FILTER (WHERE tenant_id = (SELECT tenant_id FROM params) AND revoked_at IS NULL) AS tenant_release_count,
    count(*) FILTER (WHERE tenant_id = (SELECT tenant_id FROM params) AND channel = 'internal' AND revoked_at IS NULL) AS internal_release_count,
    count(*) FILTER (WHERE tenant_id = (SELECT tenant_id FROM params) AND channel = 'stable' AND revoked_at IS NULL) AS stable_release_count,
    count(*) FILTER (WHERE tenant_id = (SELECT tenant_id FROM params) AND mandatory = true AND revoked_at IS NULL) AS mandatory_release_count,
    count(*) FILTER (WHERE tenant_id = (SELECT tenant_id FROM params) AND (artifact_hash IS NULL OR length(artifact_hash) < 32 OR signature IS NULL OR length(signature) < 8)) AS invalid_artifact_metadata_count,
    count(*) FILTER (WHERE channel NOT IN ('stable','beta','internal')) AS invalid_channel_count,
    count(*) FILTER (WHERE package_type <> 'velopack') AS invalid_package_type_count
  FROM pos.update_releases
), release_schema AS (
  SELECT
    EXISTS (
      SELECT 1 FROM pg_constraint c
      JOIN pg_class t ON t.oid = c.conrelid
      JOIN pg_namespace n ON n.oid = t.relnamespace
      WHERE n.nspname = 'pos'
        AND t.relname = 'update_releases'
        AND c.contype = 'u'
    ) AS update_release_unique_constraint_present,
    EXISTS (
      SELECT 1 FROM pg_indexes
      WHERE schemaname = 'pos'
        AND tablename = 'update_releases'
        AND indexname = 'idx_update_releases_check'
    ) AS update_release_check_index_present
), audit_release AS (
  SELECT count(*) FILTER (WHERE ae.occurred_at >= (SELECT checked_at FROM params) - interval '24 hours') AS audit_events_last_24_hours
  FROM pos.audit_events ae
  JOIN params p ON p.tenant_id = ae.tenant_id
), changes AS (
  SELECT count(*) AS sync_change_count
  FROM pos.sync_changes sc
  JOIN params p ON p.tenant_id = sc.tenant_id
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT ts.required_tables_present THEN 'required_table_missing' END,
    CASE WHEN NOT topo.tenant_active THEN 'tenant_missing_or_inactive' END,
    CASE WHEN NOT rc.release_exists THEN 'created_release_missing' END,
    CASE WHEN rc.channel <> 'internal' THEN 'created_release_not_internal_candidate' END,
    CASE WHEN rc.package_type <> 'velopack' THEN 'created_release_not_velopack' END,
    CASE WHEN rc.artifact_url = '' THEN 'created_release_missing_artifact_url' END,
    CASE WHEN length(rc.artifact_hash) < 32 THEN 'created_release_missing_artifact_hash' END,
    CASE WHEN length(rc.signature) < 8 THEN 'created_release_missing_signature' END,
    CASE WHEN rc.rollback_version = '' THEN 'created_release_missing_rollback_version' END,
    CASE WHEN rc.mandatory THEN 'created_release_must_not_be_mandatory' END,
    CASE WHEN NOT rc.universal_installer THEN 'created_release_requires_universal_installer' END,
    CASE WHEN rc.revoked_at IS NOT NULL THEN 'created_release_must_not_be_revoked' END,
    CASE WHEN NOT rc.tenant_scoped THEN 'created_release_must_be_tenant_scoped' END,
    CASE WHEN NOT rs.update_release_unique_constraint_present THEN 'update_release_unique_constraint_missing' END,
    CASE WHEN NOT rs.update_release_check_index_present THEN 'update_release_check_index_missing' END,
    CASE WHEN ri.invalid_channel_count > 0 THEN 'invalid_update_release_channel_detected' END,
    CASE WHEN ri.invalid_package_type_count > 0 THEN 'invalid_update_package_type_detected' END
  ], NULL) AS sql_blocking_reasons,
  array_remove(ARRAY[
    CASE WHEN ri.mandatory_release_count > 0 THEN 'mandatory_release_requires_release_manager_review' END,
    CASE WHEN ri.invalid_artifact_metadata_count > 0 THEN 'artifact_metadata_requires_review' END,
    CASE WHEN ri.stable_release_count = 0 THEN 'stable_channel_has_no_release_yet' END,
    CASE WHEN ar.audit_events_last_24_hours < 1 THEN 'audit_evidence_low_requires_review' END
  ], NULL) AS sql_warnings
  FROM table_status ts
  CROSS JOIN topology topo
  CROSS JOIN release_check rc
  CROSS JOIN release_inventory ri
  CROSS JOIN release_schema rs
  CROSS JOIN audit_release ar
)
SELECT json_build_object(
  'exp09SqlValidation', CASE WHEN array_length(b.sql_blocking_reasons,1) IS NULL THEN 'GO' ELSE 'NO-GO' END,
  'sqlBlockingReasons', coalesce(b.sql_blocking_reasons, ARRAY[]::text[]),
  'sqlWarnings', coalesce(b.sql_warnings, ARRAY[]::text[]),
  'requiredTablesPresent', ts.required_tables_present,
  'tenantActive', topo.tenant_active,
  'releaseExists', rc.release_exists,
  'releaseChannel', rc.channel,
  'releasePackageType', rc.package_type,
  'releaseArtifactUrl', rc.artifact_url,
  'releaseArtifactHash', rc.artifact_hash,
  'releaseSignaturePresent', length(rc.signature) >= 8,
  'releaseRollbackVersion', rc.rollback_version,
  'releaseMandatory', rc.mandatory,
  'releaseUniversalInstaller', rc.universal_installer,
  'releaseRevoked', rc.revoked_at IS NOT NULL,
  'releaseTenantScoped', rc.tenant_scoped,
  'releasePublishedAt', rc.published_at,
  'tenantReleaseCount', ri.tenant_release_count,
  'internalReleaseCount', ri.internal_release_count,
  'stableReleaseCount', ri.stable_release_count,
  'mandatoryReleaseCount', ri.mandatory_release_count,
  'invalidArtifactMetadataCount', ri.invalid_artifact_metadata_count,
  'invalidChannelCount', ri.invalid_channel_count,
  'invalidPackageTypeCount', ri.invalid_package_type_count,
  'updateReleaseUniqueConstraintPresent', rs.update_release_unique_constraint_present,
  'updateReleaseCheckIndexPresent', rs.update_release_check_index_present,
  'auditEventsLast24Hours', ar.audit_events_last_24_hours,
  'syncChangeCount', ch.sync_change_count,
  'schemaVersion', 4,
  'releaseManagementContract', 'release_management_update_channel'
)::text
FROM table_status ts
CROSS JOIN topology topo
CROSS JOIN release_check rc
CROSS JOIN release_inventory ri
CROSS JOIN release_schema rs
CROSS JOIN audit_release ar
CROSS JOIN changes ch
CROSS JOIN blockers b;
