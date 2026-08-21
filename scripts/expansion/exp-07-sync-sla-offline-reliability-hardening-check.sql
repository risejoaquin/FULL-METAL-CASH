\set ON_ERROR_STOP on

WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         now() AS checked_at,
         interval '15 minutes' AS processing_sla,
         interval '15 minutes' AS retry_sla
), table_status AS (
  SELECT bool_and(to_regclass(table_name) IS NOT NULL) AS required_tables_present
  FROM (VALUES
    ('pos.tenants'),
    ('pos.stores'),
    ('pos.terminals'),
    ('pos.sync_inbox_events'),
    ('pos.sync_changes'),
    ('pos.sync_conflicts'),
    ('pos.audit_events')
  ) required(table_name)
), inbox AS (
  SELECT sie.*
  FROM pos.sync_inbox_events sie
  JOIN params p ON p.tenant_id = sie.tenant_id
), status_counts AS (
  SELECT
    count(*) AS total_sync_events,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'received') AS received_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'processing') AS processing_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'processed') AS processed_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'duplicate') AS duplicate_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'rejected') AS rejected_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'retry_pending') AS retry_pending_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'conflict') AS conflict_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'dead_letter') AS dead_letter_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) NOT IN ('received','processing','processed','duplicate','rejected','retry_pending','conflict','dead_letter')) AS invalid_status_count,
    min(created_at) FILTER (WHERE lower(coalesce(status,'')) IN ('received','retry_pending')) AS oldest_pending_at,
    max(processed_at) AS last_processed_at
  FROM inbox
), retry_health AS (
  SELECT
    count(*) FILTER (WHERE lower(coalesce(i.status,'')) = 'retry_pending' AND i.next_retry_at IS NULL) AS retry_without_next_retry_count,
    count(*) FILTER (WHERE lower(coalesce(i.status,'')) = 'retry_pending' AND coalesce(i.next_retry_at, i.created_at) <= p.checked_at) AS retry_due_count,
    count(*) FILTER (WHERE lower(coalesce(i.status,'')) = 'retry_pending' AND i.created_at < p.checked_at - p.retry_sla) AS retry_over_sla_count,
    count(*) FILTER (WHERE lower(coalesce(i.status,'')) = 'processing' AND coalesce(i.last_attempt_at, i.created_at) < p.checked_at - p.processing_sla) AS stale_processing_count,
    count(*) FILTER (WHERE lower(coalesce(i.status,'')) = 'received' AND i.created_at < p.checked_at - p.retry_sla) AS stale_received_count,
    count(*) FILTER (WHERE i.attempts < 0 OR i.max_attempts < 1 OR i.attempts > i.max_attempts) AS invalid_attempt_shape_count
  FROM inbox i
  CROSS JOIN params p
), dead_letter_health AS (
  SELECT
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'dead_letter' AND dead_lettered_at IS NULL) AS dead_letter_without_timestamp_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'dead_letter' AND (error_code IS NULL OR error_message IS NULL)) AS dead_letter_without_error_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'dead_letter' AND replayed_at IS NOT NULL) AS replayed_dead_letter_count
  FROM inbox
), duplicate_guard AS (
  SELECT count(*) AS duplicate_batch_sequence_violation_count
  FROM (
    SELECT tenant_id, terminal_id, batch_id, sequence_number, count(*)
    FROM inbox
    WHERE batch_id IS NOT NULL AND sequence_number IS NOT NULL
    GROUP BY tenant_id, terminal_id, batch_id, sequence_number
    HAVING count(*) > 1
  ) d
), event_guard AS (
  SELECT count(*) AS duplicate_event_identity_count
  FROM (
    SELECT tenant_id, terminal_id, event_id, count(*)
    FROM inbox
    GROUP BY tenant_id, terminal_id, event_id
    HAVING count(*) > 1
  ) d
), conflict_health AS (
  SELECT
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'pending') AS pending_conflict_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'resolved') AS resolved_conflict_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'ignored') AS ignored_conflict_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) NOT IN ('pending','resolved','ignored')) AS invalid_conflict_status_count
  FROM pos.sync_conflicts sc
  JOIN params p ON p.tenant_id = sc.tenant_id
), changes_health AS (
  SELECT
    count(*) AS sync_change_count,
    max(changed_at) AS latest_change_at,
    count(*) FILTER (WHERE operation NOT IN ('create','update','delete')) AS invalid_change_operation_count
  FROM pos.sync_changes c
  JOIN params p ON p.tenant_id = c.tenant_id
), schema_health AS (
  SELECT
    count(*) FILTER (WHERE schema_version < 4) AS legacy_schema_event_count,
    count(*) FILTER (WHERE schema_version = 4) AS schema_version_4_event_count,
    max(schema_version) AS max_schema_version
  FROM inbox
), topology AS (
  SELECT
    EXISTS (SELECT 1 FROM pos.tenants t JOIN params p ON p.tenant_id = t.id WHERE t.status = 'active' AND t.deleted_at IS NULL) AS tenant_active,
    (SELECT count(*) FROM pos.stores s JOIN params p ON p.tenant_id = s.tenant_id WHERE s.status = 'active' AND s.deleted_at IS NULL) AS active_store_count,
    (SELECT count(*) FROM pos.terminals t JOIN params p ON p.tenant_id = t.tenant_id WHERE t.status = 'active' AND t.deleted_at IS NULL) AS active_terminal_count
), audit_health AS (
  SELECT count(*) FILTER (WHERE ae.occurred_at >= now() - interval '24 hours') AS audit_events_last_24_hours
  FROM pos.audit_events ae
  JOIN params p ON p.tenant_id = ae.tenant_id
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT ts.required_tables_present THEN 'required_table_missing' END,
    CASE WHEN NOT topo.tenant_active THEN 'tenant_missing_or_inactive' END,
    CASE WHEN topo.active_store_count < 2 THEN 'multi_store_sync_context_missing' END,
    CASE WHEN topo.active_terminal_count < 2 THEN 'multi_terminal_sync_context_missing' END,
    CASE WHEN sc.processed_count < 1 THEN 'processed_sync_evidence_missing' END,
    CASE WHEN ch.pending_conflict_count > 0 THEN 'pending_conflicts_block_sync_sla' END,
    CASE WHEN rh.stale_processing_count > 0 THEN 'stale_processing_events_over_sla' END,
    CASE WHEN dg.duplicate_batch_sequence_violation_count > 0 THEN 'duplicate_batch_sequence_violation' END,
    CASE WHEN eg.duplicate_event_identity_count > 0 THEN 'duplicate_event_identity_violation' END,
    CASE WHEN sc.invalid_status_count > 0 THEN 'invalid_sync_status' END,
    CASE WHEN ch.invalid_conflict_status_count > 0 THEN 'invalid_conflict_status' END,
    CASE WHEN cg.invalid_change_operation_count > 0 THEN 'invalid_sync_change_operation' END,
    CASE WHEN rh.invalid_attempt_shape_count > 0 THEN 'invalid_retry_attempt_shape' END
  ], NULL) AS sql_blocking_reasons,
  array_remove(ARRAY[
    CASE WHEN sc.retry_pending_count > 0 THEN 'retry_pending_sync_requires_monitoring' END,
    CASE WHEN rh.retry_without_next_retry_count > 0 THEN 'retry_pending_without_next_retry_requires_review' END,
    CASE WHEN rh.retry_due_count > 0 THEN 'retry_due_requires_worker_processing' END,
    CASE WHEN rh.retry_over_sla_count > 0 THEN 'retry_pending_over_sla_requires_triage' END,
    CASE WHEN sc.dead_letter_count > 0 THEN 'dead_letter_sync_requires_triage' END,
    CASE WHEN dlh.dead_letter_without_timestamp_count > 0 THEN 'dead_letter_without_timestamp_requires_review' END,
    CASE WHEN dlh.dead_letter_without_error_count > 0 THEN 'dead_letter_without_error_requires_review' END,
    CASE WHEN sh.legacy_schema_event_count > 0 THEN 'legacy_schema_events_require_compatibility_monitoring' END,
    CASE WHEN sh.schema_version_4_event_count < 1 THEN 'schema_version_4_sync_evidence_requires_review' END,
    CASE WHEN rh.stale_received_count > 0 THEN 'received_events_over_sla_require_processing_review' END
  ], NULL) AS sql_warnings
  FROM table_status ts
  CROSS JOIN topology topo
  CROSS JOIN status_counts sc
  CROSS JOIN retry_health rh
  CROSS JOIN dead_letter_health dlh
  CROSS JOIN duplicate_guard dg
  CROSS JOIN event_guard eg
  CROSS JOIN conflict_health ch
  CROSS JOIN changes_health cg
  CROSS JOIN schema_health sh
)
SELECT json_build_object(
  'exp07SqlValidation', CASE WHEN array_length(b.sql_blocking_reasons,1) IS NULL THEN 'GO' ELSE 'NO-GO' END,
  'sqlBlockingReasons', coalesce(b.sql_blocking_reasons, ARRAY[]::text[]),
  'sqlWarnings', coalesce(b.sql_warnings, ARRAY[]::text[]),
  'requiredTablesPresent', ts.required_tables_present,
  'tenantActive', topo.tenant_active,
  'activeStoreCount', topo.active_store_count,
  'activeTerminalCount', topo.active_terminal_count,
  'totalSyncEvents', sc.total_sync_events,
  'receivedSync', sc.received_count,
  'processingSync', sc.processing_count,
  'processedSync', sc.processed_count,
  'duplicateSync', sc.duplicate_count,
  'rejectedSync', sc.rejected_count,
  'retryPendingSync', sc.retry_pending_count,
  'conflictSync', sc.conflict_count,
  'deadLetterSync', sc.dead_letter_count,
  'invalidStatusCount', sc.invalid_status_count,
  'oldestPendingAt', sc.oldest_pending_at,
  'lastProcessedAt', sc.last_processed_at,
  'retryWithoutNextRetryCount', rh.retry_without_next_retry_count,
  'retryDueCount', rh.retry_due_count,
  'retryOverSlaCount', rh.retry_over_sla_count,
  'staleProcessingCount', rh.stale_processing_count,
  'staleReceivedCount', rh.stale_received_count,
  'invalidAttemptShapeCount', rh.invalid_attempt_shape_count,
  'deadLetterWithoutTimestampCount', dlh.dead_letter_without_timestamp_count,
  'deadLetterWithoutErrorCount', dlh.dead_letter_without_error_count,
  'replayedDeadLetterCount', dlh.replayed_dead_letter_count,
  'duplicateBatchSequenceViolationCount', dg.duplicate_batch_sequence_violation_count,
  'duplicateEventIdentityCount', eg.duplicate_event_identity_count,
  'pendingConflicts', ch.pending_conflict_count,
  'resolvedConflicts', ch.resolved_conflict_count,
  'ignoredConflicts', ch.ignored_conflict_count,
  'syncChangeCount', cg.sync_change_count,
  'latestChangeAt', cg.latest_change_at,
  'invalidChangeOperationCount', cg.invalid_change_operation_count,
  'legacySchemaEventCount', sh.legacy_schema_event_count,
  'schemaVersion4EventCount', sh.schema_version_4_event_count,
  'maxSchemaVersion', sh.max_schema_version,
  'auditEventsLast24Hours', ah.audit_events_last_24_hours,
  'schemaVersion', 4,
  'syncReliabilityContract', 'sync_sla_offline_reliability_hardening'
)::text
FROM table_status ts
CROSS JOIN topology topo
CROSS JOIN status_counts sc
CROSS JOIN retry_health rh
CROSS JOIN dead_letter_health dlh
CROSS JOIN duplicate_guard dg
CROSS JOIN event_guard eg
CROSS JOIN conflict_health ch
CROSS JOIN changes_health cg
CROSS JOIN schema_health sh
CROSS JOIN audit_health ah
CROSS JOIN blockers b;
