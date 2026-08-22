\set ON_ERROR_STOP on
SELECT set_config('app.tenant_id', :'tenant_id', false);

WITH p AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         :'ga04_at'::timestamptz AS ga04_at,
         now() AS checked_at
), latest_beta AS (
  SELECT r.*
  FROM pos.update_releases r
  JOIN p ON p.tenant_id=r.tenant_id
  WHERE r.channel='beta' AND r.revoked_at IS NULL
  ORDER BY r.published_at DESC
  LIMIT 1
), facts AS (
  SELECT
    (SELECT count(*) FROM pos.tenants t JOIN p ON p.tenant_id=t.id WHERE t.status='active')::bigint AS active_tenant_count,
    (SELECT count(*) FROM pos.update_releases r JOIN p ON p.tenant_id=r.tenant_id WHERE r.channel='beta' AND r.revoked_at IS NULL)::bigint AS active_beta_release_count,
    (SELECT count(*) FROM pos.update_releases r JOIN p ON p.tenant_id=r.tenant_id WHERE r.channel='stable' AND r.revoked_at IS NULL)::bigint AS active_stable_release_count,
    (SELECT count(*) FROM pos.update_releases r JOIN p ON p.tenant_id=r.tenant_id WHERE r.channel='stable' AND r.published_at>=p.ga04_at)::bigint AS stable_release_created_since_ga04_count,
    (SELECT count(*) FROM latest_beta WHERE package_type='velopack' AND mandatory=false AND universal_installer=true AND length(coalesce(artifact_hash,''))>=32 AND length(coalesce(signature,''))>=8 AND length(coalesce(rollback_version,''))>0)::bigint AS valid_beta_rollback_baseline_count,
    (SELECT version FROM latest_beta)::text AS rollback_candidate_version,
    (SELECT artifact_hash FROM latest_beta)::text AS rollback_candidate_artifact_hash,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE lower(coalesce(i.status,''))='retry_pending')::bigint AS retry_pending_count,
    (SELECT count(*) FROM pos.sync_conflicts c JOIN p ON p.tenant_id=c.tenant_id WHERE lower(coalesce(c.status,''))='pending')::bigint AS pending_conflict_count,
    (SELECT count(*) FROM pos.sync_inbox_events i JOIN p ON p.tenant_id=i.tenant_id WHERE coalesce(i.schema_version,4)<>4)::bigint AS legacy_schema_event_count,
    (SELECT count(*) FROM pos.audit_events a JOIN p ON p.tenant_id=a.tenant_id WHERE a.action='ga02.dead_letter_closed_as_historical_evidence' AND coalesce(a.after_data->>'decision','')='close_as_historical_evidence')::bigint AS historical_dead_letter_decision_audit_count,
    (SELECT count(*) FROM pos.audit_events a JOIN p ON p.tenant_id=a.tenant_id WHERE a.occurred_at>=p.checked_at-interval '24 hours')::bigint AS audit_events_last24h,
    (SELECT count(*) FROM pos.sales s JOIN p ON p.tenant_id=s.tenant_id WHERE s.status='completed')::bigint AS completed_sale_count,
    (SELECT count(*) FROM pos.inventory_ledger l JOIN p ON p.tenant_id=l.tenant_id)::bigint AS inventory_ledger_entry_count
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN active_tenant_count<>1 THEN 'tenant_missing_or_inactive' END,
    CASE WHEN active_beta_release_count<1 THEN 'active_beta_rollback_baseline_missing' END,
    CASE WHEN valid_beta_rollback_baseline_count<>1 THEN 'beta_rollback_baseline_contract_invalid' END,
    CASE WHEN coalesce(length(rollback_candidate_version),0)=0 THEN 'rollback_candidate_version_missing' END,
    CASE WHEN active_stable_release_count<>0 THEN 'stable_channel_already_promoted' END,
    CASE WHEN stable_release_created_since_ga04_count<>0 THEN 'stable_release_created_during_ga05_readiness' END,
    CASE WHEN retry_pending_count<>0 THEN 'retry_pending_sync_reopened' END,
    CASE WHEN pending_conflict_count<>0 THEN 'pending_sync_conflict' END,
    CASE WHEN legacy_schema_event_count<>0 THEN 'legacy_schema_event' END,
    CASE WHEN historical_dead_letter_decision_audit_count<1 THEN 'historical_dead_letter_decision_evidence_missing' END,
    CASE WHEN audit_events_last24h<1 THEN 'recent_audit_evidence_missing' END,
    CASE WHEN completed_sale_count<1 THEN 'commercial_data_baseline_missing' END,
    CASE WHEN inventory_ledger_entry_count<1 THEN 'inventory_ledger_baseline_missing' END
  ],NULL) AS items
  FROM facts
)
SELECT json_build_object(
  'ga05SqlContract','ga_stable_release_candidate_build_signing_provenance',
  'activeTenantCount',active_tenant_count,
  'activeBetaReleaseCount',active_beta_release_count,
  'activeStableReleaseCount',active_stable_release_count,
  'stableReleaseCreatedSinceGa04Count',stable_release_created_since_ga04_count,
  'validBetaRollbackBaselineCount',valid_beta_rollback_baseline_count,
  'rollbackCandidateVersion',rollback_candidate_version,
  'rollbackCandidateArtifactHash',rollback_candidate_artifact_hash,
  'retryPendingCount',retry_pending_count,
  'pendingConflictCount',pending_conflict_count,
  'legacySchemaEventCount',legacy_schema_event_count,
  'historicalDeadLetterDecisionAuditCount',historical_dead_letter_decision_audit_count,
  'auditEventsLast24Hours',audit_events_last24h,
  'completedSaleCount',completed_sale_count,
  'inventoryLedgerEntryCount',inventory_ledger_entry_count,
  'blockers',blockers.items,
  'ga05SqlDecision',CASE WHEN cardinality(blockers.items)=0 THEN 'GO' ELSE 'NO-GO' END,
  'schemaVersion',4,
  'syncContract','schema_version_4',
  'generalAvailabilityActivated',false,
  'checkedAt',(SELECT checked_at FROM p)
)::text
FROM facts CROSS JOIN blockers;
