\set ON_ERROR_STOP on

WITH scoped AS (
  SELECT
    :'tenant_id'::uuid AS tenant_id,
    :'store_id'::uuid AS store_id,
    :'terminal_id'::uuid AS terminal_id,
    :'recovery_inbox_id'::uuid AS recovery_inbox_id,
    :'dead_letter_inbox_id'::uuid AS dead_letter_inbox_id,
    :'conflict_id'::uuid AS conflict_id,
    :'conflict_event_id'::uuid AS conflict_event_id
), checks AS (
  SELECT
    (SELECT count(*) FROM pos.sync_inbox_events e, scoped s WHERE e.tenant_id = s.tenant_id AND e.store_id = s.store_id AND e.terminal_id = s.terminal_id AND e.id = s.recovery_inbox_id AND e.status = 'processed' AND e.event_type = 'pos.health_check') AS recovered_processed_count,
    (SELECT count(*) FROM pos.sync_inbox_events e, scoped s WHERE e.tenant_id = s.tenant_id AND e.store_id = s.store_id AND e.terminal_id = s.terminal_id AND e.id = s.dead_letter_inbox_id AND e.status = 'dead_letter' AND e.replayed_at IS NOT NULL AND e.replay_reason IS NOT NULL AND e.error_code = 'unsupported_event_type') AS retried_dead_letter_count,
    (SELECT count(*) FROM pos.sync_conflicts c, scoped s WHERE c.tenant_id = s.tenant_id AND c.id = s.conflict_id AND c.local_event_id = s.conflict_event_id AND c.status = 'resolved' AND c.resolution_strategy = 'use_server' AND c.resolved_at IS NOT NULL) AS resolved_conflict_count,
    (SELECT count(*) FROM pos.sync_inbox_events e JOIN pos.sync_conflicts c ON c.tenant_id = e.tenant_id AND c.id = e.conflict_id JOIN scoped s ON s.tenant_id = e.tenant_id WHERE e.tenant_id = s.tenant_id AND e.store_id = s.store_id AND e.terminal_id = s.terminal_id AND c.id = s.conflict_id AND e.status = 'processed') AS conflict_inbox_processed_count,
    (SELECT count(*) FROM pos.audit_events a, scoped s WHERE a.tenant_id = s.tenant_id AND a.action = 'sync.conflict.resolved' AND a.entity_type = 'sync_conflict' AND a.entity_id = s.conflict_id) AS conflict_resolution_audit_count
)
SELECT
  recovered_processed_count,
  retried_dead_letter_count,
  resolved_conflict_count,
  conflict_inbox_processed_count,
  conflict_resolution_audit_count,
  CASE
    WHEN recovered_processed_count = 1
     AND retried_dead_letter_count = 1
     AND resolved_conflict_count = 1
     AND conflict_inbox_processed_count = 1
     AND conflict_resolution_audit_count >= 1
    THEN 'GO'
    ELSE 'NO-GO'
  END AS pilot_06_go_no_go
FROM checks;
