\set ON_ERROR_STOP on

WITH p AS (
  SELECT :'tenant_id'::uuid AS tenant_id, now() AS checked_at
), sync AS (
  SELECT
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'retry_pending')::bigint AS retry_pending_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'retry_pending' AND coalesce(next_retry_at, created_at) <= now())::bigint AS retry_due_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'dead_letter')::bigint AS dead_letter_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'dead_letter' AND error_code IS NOT NULL AND error_message IS NOT NULL AND dead_lettered_at IS NOT NULL)::bigint AS dead_letter_with_evidence_count,
    count(*) FILTER (WHERE lower(coalesce(status,'')) = 'processing' AND coalesce(last_attempt_at, created_at) < now() - interval '15 minutes')::bigint AS stale_processing_count
  FROM pos.sync_inbox_events e JOIN p ON p.tenant_id = e.tenant_id
), conflicts AS (
  SELECT count(*) FILTER (WHERE lower(coalesce(status,'')) = 'pending')::bigint AS pending_conflict_count
  FROM pos.sync_conflicts c JOIN p ON p.tenant_id = c.tenant_id
), ops AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.cash_shifts cs JOIN p ON p.tenant_id = cs.tenant_id WHERE lower(coalesce(cs.status,'')) = 'open') AS open_shift_count,
    (SELECT count(*)::bigint FROM pos.cash_shifts cs JOIN p ON p.tenant_id = cs.tenant_id WHERE cs.closed_at >= now() - interval '24 hours' AND coalesce(cs.difference_cents,0) <> 0) AS cash_difference_last_24h_count,
    (SELECT count(*)::bigint FROM pos.payments py JOIN p ON p.tenant_id = py.tenant_id WHERE lower(coalesce(py.status,'')) = 'failed' AND py.created_at >= now() - interval '24 hours') AS failed_payments_last_24h_count
), audit AS (
  SELECT
    count(*)::bigint AS audit_event_count,
    count(*) FILTER (WHERE a.occurred_at >= now() - interval '24 hours')::bigint AS audit_events_last_24h
  FROM pos.audit_events a JOIN p ON p.tenant_id = a.tenant_id
), topology AS (
  SELECT
    (SELECT count(*)::bigint FROM pos.stores s JOIN p ON p.tenant_id = s.tenant_id WHERE s.status='active' AND s.deleted_at IS NULL) AS active_store_count,
    (SELECT count(*)::bigint FROM pos.terminals t JOIN p ON p.tenant_id = t.tenant_id WHERE t.status='active' AND t.deleted_at IS NULL) AS active_terminal_count
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN t.active_store_count < 1 THEN 'active_store_missing' END,
    CASE WHEN t.active_terminal_count < 1 THEN 'active_terminal_missing' END,
    CASE WHEN a.audit_event_count < 1 THEN 'audit_evidence_missing' END,
    CASE WHEN s.stale_processing_count > 0 THEN 'stale_processing_requires_incident' END,
    CASE WHEN c.pending_conflict_count > 0 THEN 'pending_conflict_requires_incident' END,
    CASE WHEN s.dead_letter_count > s.dead_letter_with_evidence_count THEN 'dead_letter_evidence_incomplete' END
  ], NULL) AS items
  FROM sync s CROSS JOIN conflicts c CROSS JOIN audit a CROSS JOIN topology t
), conditions AS (
  SELECT array_remove(ARRAY[
    CASE WHEN s.retry_pending_count > 0 THEN 'retry_pending_sync_requires_triage' END,
    CASE WHEN s.retry_due_count > 0 THEN 'retry_due_requires_worker_or_manual_retry' END,
    CASE WHEN s.dead_letter_count > 0 THEN 'dead_letter_sync_requires_triage' END,
    CASE WHEN o.open_shift_count > 0 THEN 'open_cash_shift_requires_daily_review' END,
    CASE WHEN o.cash_difference_last_24h_count > 0 THEN 'cash_difference_requires_support_review' END,
    CASE WHEN o.failed_payments_last_24h_count > 0 THEN 'failed_payment_requires_support_review' END
  ], NULL) AS items
  FROM sync s CROSS JOIN ops o
)
SELECT json_build_object(
  'beta05SqlDecision', CASE WHEN array_length(b.items,1) IS NULL THEN 'GO' ELSE 'NO-GO' END,
  'blockers', coalesce(b.items, ARRAY[]::text[]),
  'conditions', coalesce(cd.items, ARRAY[]::text[]),
  'retryPendingSync', s.retry_pending_count,
  'retryDueCount', s.retry_due_count,
  'deadLetterSync', s.dead_letter_count,
  'deadLetterWithEvidenceCount', s.dead_letter_with_evidence_count,
  'staleProcessingCount', s.stale_processing_count,
  'pendingConflictCount', c.pending_conflict_count,
  'openShiftCount', o.open_shift_count,
  'cashDifferenceLast24HoursCount', o.cash_difference_last_24h_count,
  'failedPaymentsLast24Hours', o.failed_payments_last_24h_count,
  'auditEventCount', a.audit_event_count,
  'auditEventsLast24Hours', a.audit_events_last_24h,
  'activeStoreCount', t.active_store_count,
  'activeTerminalCount', t.active_terminal_count,
  'schemaVersion', 4,
  'supportDrillContract', 'beta_support_operations_drill'
)::text
FROM sync s CROSS JOIN conflicts c CROSS JOIN ops o CROSS JOIN audit a CROSS JOIN topology t CROSS JOIN blockers b CROSS JOIN conditions cd;
