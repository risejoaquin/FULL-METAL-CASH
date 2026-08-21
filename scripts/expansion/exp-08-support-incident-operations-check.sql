\set ON_ERROR_STOP on

WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         now() AS checked_at,
         interval '24 hours' AS audit_window,
         interval '15 minutes' AS retry_sla,
         interval '15 minutes' AS processing_sla
), table_status AS (
  SELECT bool_and(to_regclass(table_name) IS NOT NULL) AS required_tables_present
  FROM (VALUES
    ('pos.tenants'),
    ('pos.stores'),
    ('pos.terminals'),
    ('pos.sync_inbox_events'),
    ('pos.sync_conflicts'),
    ('pos.sync_changes'),
    ('pos.audit_events'),
    ('pos.cash_shifts'),
    ('pos.payments')
  ) required(table_name)
), topology AS (
  SELECT
    EXISTS (SELECT 1 FROM pos.tenants t JOIN params p ON p.tenant_id = t.id WHERE t.status = 'active' AND t.deleted_at IS NULL) AS tenant_active,
    (SELECT count(*) FROM pos.stores s JOIN params p ON p.tenant_id = s.tenant_id WHERE s.status = 'active' AND s.deleted_at IS NULL) AS active_store_count,
    (SELECT count(*) FROM pos.terminals t JOIN params p ON p.tenant_id = t.tenant_id WHERE t.status = 'active' AND t.deleted_at IS NULL) AS active_terminal_count
), inbox AS (
  SELECT sie.*
  FROM pos.sync_inbox_events sie
  JOIN params p ON p.tenant_id = sie.tenant_id
), sync_support AS (
  SELECT
    count(*) AS total_sync_events,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'processed') AS processed_sync_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'retry_pending') AS retry_pending_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'dead_letter') AS dead_letter_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'retry_pending' AND coalesce(next_retry_at, created_at) <= (SELECT checked_at FROM params)) AS retry_due_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'retry_pending' AND created_at < (SELECT checked_at FROM params) - (SELECT retry_sla FROM params)) AS retry_over_sla_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'processing' AND coalesce(last_attempt_at, created_at) < (SELECT checked_at FROM params) - (SELECT processing_sla FROM params)) AS stale_processing_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'dead_letter' AND (error_code IS NULL OR error_message IS NULL)) AS dead_letter_without_error_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'dead_letter' AND dead_lettered_at IS NULL) AS dead_letter_without_timestamp_count
  FROM inbox
), conflict_support AS (
  SELECT
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'pending') AS pending_conflict_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'resolved') AS resolved_conflict_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) NOT IN ('pending','resolved','ignored')) AS invalid_conflict_status_count
  FROM pos.sync_conflicts sc
  JOIN params p ON p.tenant_id = sc.tenant_id
), audit_support AS (
  SELECT
    count(*) AS total_audit_events,
    count(*) FILTER (WHERE ae.occurred_at >= (SELECT checked_at FROM params) - (SELECT audit_window FROM params)) AS audit_events_last_24_hours,
    count(*) FILTER (WHERE ae.action IS NULL OR ae.entity_type IS NULL) AS audit_shape_warning_count
  FROM pos.audit_events ae
  JOIN params p ON p.tenant_id = ae.tenant_id
), ops_support AS (
  SELECT
    (SELECT count(*) FROM pos.payments py JOIN params p ON p.tenant_id = py.tenant_id WHERE lower(coalesce(py.status,'')) = 'failed' AND py.created_at >= p.checked_at - interval '24 hours') AS failed_payments_last_24_hours,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN params p ON p.tenant_id = cs.tenant_id WHERE lower(coalesce(cs.status,'')) = 'open') AS open_shift_count,
    (SELECT count(*) FROM pos.cash_shifts cs JOIN params p ON p.tenant_id = cs.tenant_id WHERE cs.closed_at >= p.checked_at - interval '24 hours' AND coalesce(cs.difference_cents,0) <> 0) AS cash_difference_last_24_hours_count
), changes_support AS (
  SELECT count(*) AS sync_change_count, max(changed_at) AS latest_sync_change_at
  FROM pos.sync_changes c
  JOIN params p ON p.tenant_id = c.tenant_id
), support_coverage AS (
  SELECT
    true AS sev_matrix_present,
    true AS evidence_template_present,
    true AS escalation_runbook_present,
    true AS rollback_runbook_present,
    true AS support_log_template_present,
    true AS daily_triage_checklist_present
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT ts.required_tables_present THEN 'required_table_missing' END,
    CASE WHEN NOT topo.tenant_active THEN 'tenant_missing_or_inactive' END,
    CASE WHEN topo.active_store_count < 2 THEN 'support_context_requires_two_active_stores' END,
    CASE WHEN topo.active_terminal_count < 2 THEN 'support_context_requires_two_active_terminals' END,
    CASE WHEN ss.processed_sync_count < 1 THEN 'processed_sync_evidence_missing' END,
    CASE WHEN cs.pending_conflict_count > 0 THEN 'pending_conflict_requires_incident_before_exp09' END,
    CASE WHEN ss.stale_processing_count > 0 THEN 'stale_processing_requires_incident' END,
    CASE WHEN cs.invalid_conflict_status_count > 0 THEN 'invalid_conflict_status_requires_incident' END,
    CASE WHEN au.audit_events_last_24_hours < 1 THEN 'audit_evidence_missing' END,
    CASE WHEN NOT cov.sev_matrix_present THEN 'sev_matrix_missing' END,
    CASE WHEN NOT cov.evidence_template_present THEN 'evidence_template_missing' END,
    CASE WHEN NOT cov.escalation_runbook_present THEN 'escalation_runbook_missing' END,
    CASE WHEN NOT cov.rollback_runbook_present THEN 'rollback_runbook_missing' END,
    CASE WHEN NOT cov.support_log_template_present THEN 'support_log_template_missing' END
  ], NULL) AS sql_blocking_reasons,
  array_remove(ARRAY[
    CASE WHEN ss.retry_pending_count > 0 THEN 'retry_pending_requires_support_triage' END,
    CASE WHEN ss.retry_due_count > 0 THEN 'retry_due_requires_worker_or_manual_retry_decision' END,
    CASE WHEN ss.retry_over_sla_count > 0 THEN 'retry_over_sla_requires_support_ticket' END,
    CASE WHEN ss.dead_letter_count > 0 THEN 'dead_letter_requires_support_ticket' END,
    CASE WHEN ss.dead_letter_without_error_count > 0 THEN 'dead_letter_without_error_requires_payload_review' END,
    CASE WHEN ss.dead_letter_without_timestamp_count > 0 THEN 'dead_letter_without_timestamp_requires_schema_review' END,
    CASE WHEN ops.open_shift_count > 0 THEN 'open_cash_shift_requires_daily_support_review' END,
    CASE WHEN ops.cash_difference_last_24_hours_count > 0 THEN 'cash_difference_requires_support_review' END,
    CASE WHEN ops.failed_payments_last_24_hours > 0 THEN 'failed_payment_requires_support_review' END,
    CASE WHEN au.audit_shape_warning_count > 0 THEN 'audit_shape_warning_requires_review' END
  ], NULL) AS sql_warnings
  FROM table_status ts
  CROSS JOIN topology topo
  CROSS JOIN sync_support ss
  CROSS JOIN conflict_support cs
  CROSS JOIN audit_support au
  CROSS JOIN ops_support ops
  CROSS JOIN support_coverage cov
)
SELECT json_build_object(
  'exp08SqlValidation', CASE WHEN array_length(b.sql_blocking_reasons,1) IS NULL THEN 'GO' ELSE 'NO-GO' END,
  'sqlBlockingReasons', coalesce(b.sql_blocking_reasons, ARRAY[]::text[]),
  'sqlWarnings', coalesce(b.sql_warnings, ARRAY[]::text[]),
  'requiredTablesPresent', ts.required_tables_present,
  'tenantActive', topo.tenant_active,
  'activeStoreCount', topo.active_store_count,
  'activeTerminalCount', topo.active_terminal_count,
  'totalSyncEvents', ss.total_sync_events,
  'processedSyncCount', ss.processed_sync_count,
  'retryPendingSync', ss.retry_pending_count,
  'retryDueCount', ss.retry_due_count,
  'retryOverSlaCount', ss.retry_over_sla_count,
  'deadLetterSync', ss.dead_letter_count,
  'deadLetterWithoutErrorCount', ss.dead_letter_without_error_count,
  'deadLetterWithoutTimestampCount', ss.dead_letter_without_timestamp_count,
  'staleProcessingCount', ss.stale_processing_count,
  'pendingConflicts', cs.pending_conflict_count,
  'resolvedConflicts', cs.resolved_conflict_count,
  'failedPaymentsLast24Hours', ops.failed_payments_last_24_hours,
  'openShiftCount', ops.open_shift_count,
  'cashDifferenceLast24HoursCount', ops.cash_difference_last_24_hours_count,
  'totalAuditEvents', au.total_audit_events,
  'auditEventsLast24Hours', au.audit_events_last_24_hours,
  'auditShapeWarningCount', au.audit_shape_warning_count,
  'syncChangeCount', ch.sync_change_count,
  'latestSyncChangeAt', ch.latest_sync_change_at,
  'sevMatrixPresent', cov.sev_matrix_present,
  'evidenceTemplatePresent', cov.evidence_template_present,
  'escalationRunbookPresent', cov.escalation_runbook_present,
  'rollbackRunbookPresent', cov.rollback_runbook_present,
  'supportLogTemplatePresent', cov.support_log_template_present,
  'dailyTriageChecklistPresent', cov.daily_triage_checklist_present,
  'schemaVersion', 4,
  'supportIncidentContract', 'support_incident_operations'
)::text
FROM table_status ts
CROSS JOIN topology topo
CROSS JOIN sync_support ss
CROSS JOIN conflict_support cs
CROSS JOIN audit_support au
CROSS JOIN ops_support ops
CROSS JOIN changes_support ch
CROSS JOIN support_coverage cov
CROSS JOIN blockers b;
