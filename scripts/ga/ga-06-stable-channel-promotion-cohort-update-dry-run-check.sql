\set ON_ERROR_STOP on
SELECT set_config('app.tenant_id', :'tenant_id', false);
WITH p AS (
 SELECT :'tenant_id'::uuid tenant_id, :'release_version'::text release_version,
        :'artifact_hash'::text artifact_hash, :'artifact_url'::text artifact_url,
        :'signature'::text signature, :'rollback_version'::text rollback_version,
        :'target_terminal_id'::uuid target_terminal_id, :'outside_terminal_id'::uuid outside_terminal_id
), chain AS (
 SELECT r.* FROM pos.update_releases r JOIN p ON r.tenant_id=p.tenant_id
 WHERE r.version=p.release_version AND r.package_type='velopack' AND r.channel IN ('internal','beta','stable') AND r.revoked_at IS NULL
), facts AS (
 SELECT
  (SELECT count(*) FROM pos.update_releases r JOIN p ON r.tenant_id=p.tenant_id WHERE r.channel='stable' AND r.revoked_at IS NULL)::bigint active_stable_release_count,
  (SELECT count(*) FROM chain WHERE channel='stable' AND mandatory=false)::bigint stable_non_mandatory_count,
  (SELECT count(*) FROM chain WHERE channel='stable' AND tenant_id IS NOT NULL)::bigint stable_tenant_scoped_count,
  (SELECT count(*) FROM chain WHERE universal_installer=true)::bigint universal_installer_count,
  (SELECT count(DISTINCT artifact_hash) FROM chain)::bigint distinct_artifact_hash_count,
  (SELECT count(DISTINCT artifact_url) FROM chain)::bigint distinct_artifact_url_count,
  (SELECT count(DISTINCT signature) FROM chain)::bigint distinct_signature_count,
  (SELECT count(*) FROM chain)::bigint chain_release_count,
  (SELECT count(*) FROM chain WHERE artifact_hash=(SELECT artifact_hash FROM p) AND artifact_url=(SELECT artifact_url FROM p) AND signature=(SELECT signature FROM p) AND rollback_version IS NOT DISTINCT FROM (SELECT rollback_version FROM p) AND mandatory=false AND universal_installer=true AND tenant_id=(SELECT tenant_id FROM p))::bigint promotion_artifact_match_count,
  (SELECT count(*) FROM chain WHERE channel='stable')::bigint exact_stable_rc_count,
  (SELECT count(*) FROM pos.update_release_targets rt JOIN chain r ON r.id=rt.release_id JOIN p ON p.tenant_id=rt.tenant_id WHERE rt.terminal_id=p.target_terminal_id)::bigint cohort_target_count,
  (SELECT count(*) FROM pos.update_release_targets rt JOIN chain r ON r.id=rt.release_id JOIN p ON p.tenant_id=rt.tenant_id WHERE rt.terminal_id<>p.target_terminal_id)::bigint out_of_cohort_target_count,
  (SELECT count(*) FROM pos.update_release_targets rt JOIN pos.update_releases r ON r.id=rt.release_id JOIN pos.terminals t ON t.id=rt.terminal_id JOIN p ON true WHERE rt.tenant_id<>p.tenant_id OR r.tenant_id IS DISTINCT FROM rt.tenant_id OR t.tenant_id<>rt.tenant_id)::bigint target_tenant_mismatch_count,
  (SELECT count(*) FROM pos.terminals t JOIN p ON p.tenant_id=t.tenant_id WHERE t.id=p.target_terminal_id AND t.status='active' AND coalesce(t.app_version,'')<>'')::bigint compatible_target_terminal_count,
  (SELECT count(*) FROM pos.terminals t JOIN p ON p.tenant_id=t.tenant_id WHERE t.id=p.outside_terminal_id AND t.status='active')::bigint outside_terminal_count,
  (SELECT count(*) FROM pos.update_releases r JOIN p ON r.tenant_id=p.tenant_id WHERE r.channel='beta' AND r.version=p.rollback_version AND r.revoked_at IS NULL)::bigint rollback_target_count,
  (SELECT count(DISTINCT r.id) FROM pos.audit_events a JOIN chain r ON a.entity_id::text=r.id::text WHERE a.action IN ('updates.release.created','updates.release.reconciled'))::bigint release_audit_count,
  (SELECT count(DISTINCT r.id) FROM pos.audit_events a JOIN chain r ON a.entity_id::text=r.id::text WHERE a.action='updates.release.cohort.targeted')::bigint cohort_audit_count,
  (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON i.tenant_id=p.tenant_id WHERE lower(coalesce(i.status,''))='retry_pending')::bigint retry_pending_count,
  (SELECT count(*) FROM pos.sync_conflicts c JOIN p ON c.tenant_id=p.tenant_id WHERE lower(coalesce(c.status,''))='pending')::bigint pending_conflict_count,
  (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON i.tenant_id=p.tenant_id WHERE coalesce(i.schema_version,4)<>4)::bigint legacy_schema_event_count
), blockers AS (
 SELECT array_remove(ARRAY[
  CASE WHEN active_stable_release_count<1 THEN 'active_stable_release_missing' END,
  CASE WHEN exact_stable_rc_count<>1 THEN 'stable_rc_identity_count_invalid' END,
  CASE WHEN stable_non_mandatory_count<>1 THEN 'stable_release_must_be_non_mandatory' END,
  CASE WHEN stable_tenant_scoped_count<>1 THEN 'stable_release_must_be_tenant_scoped' END,
  CASE WHEN chain_release_count<>3 THEN 'internal_beta_stable_chain_incomplete' END,
  CASE WHEN universal_installer_count<>3 THEN 'promotion_chain_not_universal_velopack' END,
  CASE WHEN distinct_artifact_hash_count<>1 OR distinct_artifact_url_count<>1 OR distinct_signature_count<>1 THEN 'promotion_changed_artifact_identity' END,
  CASE WHEN promotion_artifact_match_count<>3 THEN 'ga05_artifact_identity_mismatch' END,
  CASE WHEN cohort_target_count<>3 THEN 'cohort_targeting_incomplete' END,
  CASE WHEN out_of_cohort_target_count<>0 THEN 'rollout_target_outside_cohort' END,
  CASE WHEN target_tenant_mismatch_count<>0 THEN 'cohort_target_tenant_mismatch' END,
  CASE WHEN compatible_target_terminal_count<>1 THEN 'compatible_target_terminal_missing' END,
  CASE WHEN outside_terminal_count<>1 THEN 'outside_cohort_terminal_missing' END,
  CASE WHEN rollback_target_count<1 THEN 'rollback_target_missing' END,
  CASE WHEN release_audit_count<3 THEN 'release_promotion_audit_missing' END,
  CASE WHEN cohort_audit_count<3 THEN 'cohort_target_audit_missing' END,
  CASE WHEN retry_pending_count<>0 THEN 'retry_pending_sync_reopened' END,
  CASE WHEN pending_conflict_count<>0 THEN 'pending_sync_conflict' END,
  CASE WHEN legacy_schema_event_count<>0 THEN 'legacy_schema_event' END
 ],NULL) items FROM facts
)
SELECT json_build_object(
 'ga06SqlContract','ga_stable_channel_promotion_cohort_update_dry_run',
 'activeStableReleaseCount',active_stable_release_count,'stableNonMandatoryCount',stable_non_mandatory_count,
 'stableTenantScopedCount',stable_tenant_scoped_count,'chainReleaseCount',chain_release_count,
 'promotionArtifactMatchCount',promotion_artifact_match_count,'cohortTargetCount',cohort_target_count,
 'outOfCohortTargetCount',out_of_cohort_target_count,'targetTenantMismatchCount',target_tenant_mismatch_count,'compatibleTargetTerminalCount',compatible_target_terminal_count,
 'outsideTerminalCount',outside_terminal_count,'rollbackTargetCount',rollback_target_count,
 'releaseAuditCount',release_audit_count,'cohortAuditCount',cohort_audit_count,
 'retryPendingCount',retry_pending_count,'pendingConflictCount',pending_conflict_count,'legacySchemaEventCount',legacy_schema_event_count,
 'blockers',blockers.items,'ga06SqlDecision',CASE WHEN cardinality(blockers.items)=0 THEN 'GO' ELSE 'NO-GO' END,
 'schemaVersion',4,'syncContract','schema_version_4','generalAvailabilityActivated',false
)::text FROM facts CROSS JOIN blockers;
