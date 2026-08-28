\set ON_ERROR_STOP on
SELECT set_config('app.tenant_id', :'tenant_id', false);
WITH counts AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.tenants WHERE id=:'tenant_id'::uuid) tenant_count,
    (SELECT count(*)::bigint FROM pos.stores WHERE tenant_id=:'tenant_id'::uuid) store_count,
    (SELECT count(*)::bigint FROM pos.terminals WHERE tenant_id=:'tenant_id'::uuid) terminal_count,
    (SELECT count(*)::bigint FROM pos.users WHERE tenant_id=:'tenant_id'::uuid) user_count,
    (SELECT count(*)::bigint FROM pos.sales WHERE tenant_id=:'tenant_id'::uuid) sales_count,
    (SELECT count(*)::bigint FROM pos.payments WHERE tenant_id=:'tenant_id'::uuid) payment_count,
    (SELECT count(*)::bigint FROM pos.digital_receipts WHERE tenant_id=:'tenant_id'::uuid) receipt_count,
    (SELECT count(*)::bigint FROM pos.returns WHERE tenant_id=:'tenant_id'::uuid) return_count,
    (SELECT count(*)::bigint FROM pos.return_refunds WHERE tenant_id=:'tenant_id'::uuid) refund_count,
    (SELECT count(*)::bigint FROM pos.inventory_ledger WHERE tenant_id=:'tenant_id'::uuid) inventory_ledger_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id=:'tenant_id'::uuid) sync_inbox_count,
    (SELECT count(*)::bigint FROM pos.audit_events WHERE tenant_id=:'tenant_id'::uuid) audit_count,
    (SELECT count(*)::bigint FROM pos.update_releases WHERE tenant_id=:'tenant_id'::uuid) release_count,
    (SELECT count(*)::bigint FROM pos.update_release_targets WHERE tenant_id=:'tenant_id'::uuid) release_target_count,
    (SELECT count(*)::bigint FROM pos.update_releases WHERE tenant_id=:'tenant_id'::uuid AND channel='stable' AND revoked_at IS NULL) active_stable_count,
    (SELECT count(*)::bigint FROM pos.update_releases WHERE tenant_id=:'tenant_id'::uuid AND channel='stable' AND revoked_at IS NULL AND mandatory=false AND universal_installer=true) safe_stable_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id=:'tenant_id'::uuid AND status='retry_pending') retry_pending_count,
    (SELECT count(*)::bigint FROM pos.sync_conflicts WHERE tenant_id=:'tenant_id'::uuid AND status='pending') pending_conflict_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events WHERE tenant_id=:'tenant_id'::uuid AND schema_version<>4) legacy_schema_count
), stable AS (
  SELECT id,version,artifact_url,artifact_hash,signature,rollback_version,mandatory,universal_installer
  FROM pos.update_releases
  WHERE tenant_id=:'tenant_id'::uuid AND channel='stable' AND revoked_at IS NULL
  ORDER BY published_at DESC LIMIT 1
), rollback AS (
  SELECT count(*)::bigint available_count,
         count(*) FILTER (WHERE universal_installer=true AND mandatory=false AND artifact_url<>'' AND artifact_hash<>'' AND signature<>'')::bigint valid_artifact_count
  FROM pos.update_releases r
  WHERE r.tenant_id=:'tenant_id'::uuid AND r.version=(SELECT rollback_version FROM stable) AND r.package_type='velopack' AND r.revoked_at IS NULL
), blockers AS (
 SELECT array_remove(ARRAY[
   CASE WHEN (SELECT tenant_count FROM counts)<>1 THEN 'tenant_missing' END,
   CASE WHEN (SELECT active_stable_count FROM counts)<1 THEN 'stable_release_missing' END,
   CASE WHEN (SELECT safe_stable_count FROM counts)<1 THEN 'stable_release_not_safe' END,
   CASE WHEN coalesce((SELECT rollback_version FROM stable),'')='' THEN 'rollback_version_missing' END,
   CASE WHEN (SELECT available_count FROM rollback)<1 THEN 'rollback_release_missing' END,
   CASE WHEN (SELECT valid_artifact_count FROM rollback)<1 THEN 'rollback_artifact_invalid' END,
   CASE WHEN (SELECT retry_pending_count FROM counts)<>0 THEN 'retry_pending_sync' END,
   CASE WHEN (SELECT pending_conflict_count FROM counts)<>0 THEN 'pending_sync_conflicts' END,
   CASE WHEN (SELECT legacy_schema_count FROM counts)<>0 THEN 'legacy_sync_schema' END
 ],NULL) value
)
SELECT json_build_object(
 'capturedAt',clock_timestamp(),
 'tenantCount',(SELECT tenant_count FROM counts),'storeCount',(SELECT store_count FROM counts),'terminalCount',(SELECT terminal_count FROM counts),'userCount',(SELECT user_count FROM counts),
 'salesCount',(SELECT sales_count FROM counts),'paymentCount',(SELECT payment_count FROM counts),'receiptCount',(SELECT receipt_count FROM counts),'returnCount',(SELECT return_count FROM counts),'refundCount',(SELECT refund_count FROM counts),
 'inventoryLedgerCount',(SELECT inventory_ledger_count FROM counts),'syncInboxCount',(SELECT sync_inbox_count FROM counts),'auditCount',(SELECT audit_count FROM counts),'releaseCount',(SELECT release_count FROM counts),'releaseTargetCount',(SELECT release_target_count FROM counts),
 'activeStableReleaseCount',(SELECT active_stable_count FROM counts),'stableReleaseId',(SELECT id FROM stable),'stableVersion',(SELECT version FROM stable),'rollbackVersion',(SELECT rollback_version FROM stable),
 'rollbackReleaseAvailableCount',(SELECT available_count FROM rollback),'rollbackArtifactValidCount',(SELECT valid_artifact_count FROM rollback),
 'retryPendingCount',(SELECT retry_pending_count FROM counts),'pendingConflictCount',(SELECT pending_conflict_count FROM counts),'legacySchemaEventCount',(SELECT legacy_schema_count FROM counts),
 'blockers',(SELECT value FROM blockers),
 'ga07SqlDecision',CASE WHEN cardinality((SELECT value FROM blockers))=0 THEN 'GO' ELSE 'NO-GO' END,
 'schemaVersion',4,'syncContract','schema_version_4','generalAvailabilityActivated',false
)::text;
