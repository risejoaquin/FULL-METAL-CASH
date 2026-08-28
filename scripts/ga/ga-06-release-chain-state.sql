\set ON_ERROR_STOP on
SELECT set_config('app.tenant_id', :'tenant_id', false);
WITH p AS (
  SELECT :'tenant_id'::uuid tenant_id, :'release_version'::text release_version,
         :'artifact_hash'::text artifact_hash, :'artifact_url'::text artifact_url,
         :'signature'::text signature, :'rollback_version'::text rollback_version, :'target_terminal_id'::uuid target_terminal_id
), releases AS (
  SELECT r.* FROM pos.update_releases r JOIN p ON r.tenant_id=p.tenant_id
  WHERE r.version=p.release_version AND r.package_type='velopack' AND r.channel IN ('internal','beta','stable')
), facts AS (
  SELECT
    (SELECT id::text FROM releases WHERE channel='internal' AND revoked_at IS NULL LIMIT 1) internal_release_id,
    (SELECT id::text FROM releases WHERE channel='beta' AND revoked_at IS NULL LIMIT 1) beta_release_id,
    (SELECT id::text FROM releases WHERE channel='stable' AND revoked_at IS NULL LIMIT 1) stable_release_id,
    (SELECT count(*) FROM releases WHERE revoked_at IS NULL)::bigint active_chain_release_count,
    (SELECT count(*) FROM releases WHERE revoked_at IS NULL AND (artifact_hash<>(SELECT artifact_hash FROM p) OR artifact_url<>(SELECT artifact_url FROM p) OR signature<>(SELECT signature FROM p) OR rollback_version IS DISTINCT FROM (SELECT rollback_version FROM p) OR mandatory=true OR universal_installer=false OR tenant_id IS NULL))::bigint mismatched_release_count,
    (SELECT count(*) FROM pos.update_release_targets rt JOIN releases r ON r.id=rt.release_id JOIN p ON p.tenant_id=rt.tenant_id WHERE rt.terminal_id=p.target_terminal_id AND r.revoked_at IS NULL)::bigint expected_target_count,
    (SELECT count(*) FROM pos.update_release_targets rt JOIN releases r ON r.id=rt.release_id JOIN p ON p.tenant_id=rt.tenant_id WHERE rt.terminal_id=p.target_terminal_id AND r.channel='internal' AND r.revoked_at IS NULL)::bigint internal_target_count,
    (SELECT count(*) FROM pos.update_release_targets rt JOIN releases r ON r.id=rt.release_id JOIN p ON p.tenant_id=rt.tenant_id WHERE rt.terminal_id=p.target_terminal_id AND r.channel='beta' AND r.revoked_at IS NULL)::bigint beta_target_count,
    (SELECT count(*) FROM pos.update_release_targets rt JOIN releases r ON r.id=rt.release_id JOIN p ON p.tenant_id=rt.tenant_id WHERE rt.terminal_id=p.target_terminal_id AND r.channel='stable' AND r.revoked_at IS NULL)::bigint stable_target_count,
    (SELECT count(*) FROM pos.update_release_targets rt JOIN releases r ON r.id=rt.release_id JOIN p ON p.tenant_id=rt.tenant_id WHERE rt.terminal_id<>p.target_terminal_id AND r.revoked_at IS NULL)::bigint unexpected_target_count,
    (SELECT count(*) FROM pos.update_release_targets rt JOIN pos.update_releases r ON r.id=rt.release_id JOIN pos.terminals t ON t.id=rt.terminal_id WHERE rt.tenant_id<>(SELECT tenant_id FROM p) OR r.tenant_id IS DISTINCT FROM rt.tenant_id OR t.tenant_id<>rt.tenant_id)::bigint target_tenant_mismatch_count,
    (SELECT coalesce(json_agg(json_build_object(
      'id', r.id::text,
      'channel', r.channel,
      'artifactUrlActual', r.artifact_url,
      'artifactUrlExpected', (SELECT artifact_url FROM p),
      'artifactHashActual', r.artifact_hash,
      'artifactHashExpected', (SELECT artifact_hash FROM p),
      'signatureActual', r.signature,
      'signatureExpected', (SELECT signature FROM p),
      'rollbackVersionActual', r.rollback_version,
      'rollbackVersionExpected', (SELECT rollback_version FROM p),
      'mandatoryActual', r.mandatory,
      'mandatoryExpected', false,
      'universalInstallerActual', r.universal_installer,
      'universalInstallerExpected', true,
      'tenantScopedActual', r.tenant_id IS NOT NULL,
      'tenantScopedExpected', true,
      'revokedAt', r.revoked_at,
      'artifactUrlMatch', r.artifact_url=(SELECT artifact_url FROM p),
      'artifactHashMatch', r.artifact_hash=(SELECT artifact_hash FROM p),
      'signatureMatch', r.signature=(SELECT signature FROM p),
      'rollbackVersionMatch', r.rollback_version IS NOT DISTINCT FROM (SELECT rollback_version FROM p),
      'mandatoryMatch', r.mandatory=false,
      'universalInstallerMatch', r.universal_installer=true,
      'tenantScopeMatch', r.tenant_id=(SELECT tenant_id FROM p),
      'activeMatch', r.revoked_at IS NULL
    ) ORDER BY r.channel), '[]'::json) FROM releases r) release_identity_diagnostics
)
SELECT json_build_object(
 'internalReleaseId',internal_release_id,'betaReleaseId',beta_release_id,'stableReleaseId',stable_release_id,
 'activeChainReleaseCount',active_chain_release_count,'mismatchedReleaseCount',mismatched_release_count,
 'expectedTargetCount',expected_target_count,'internalTargetCount',internal_target_count,'betaTargetCount',beta_target_count,'stableTargetCount',stable_target_count,
 'unexpectedTargetCount',unexpected_target_count,'targetTenantMismatchCount',target_tenant_mismatch_count,
 'releaseIdentityDiagnostics',release_identity_diagnostics
)::text FROM facts;
