\set ON_ERROR_STOP on
WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id
), candidates AS (
  SELECT cs.id, cs.tenant_id, cs.terminal_id, cs.opened_by_user_id,
         cs.status, cs.expected_cash_cents, cs.counted_cash_cents, cs.difference_cents,
         cs.opened_at, t.fingerprint
  FROM pos.cash_shifts cs
  JOIN params p ON p.tenant_id = cs.tenant_id
  JOIN pos.terminals t ON t.tenant_id = cs.tenant_id AND t.id = cs.terminal_id
  WHERE lower(coalesce(cs.status,'')) = 'open'
    AND cs.opened_at < now() - interval '24 hours'
    AND (t.fingerprint LIKE 'pilot-%'
      OR t.fingerprint LIKE 'exp-%'
      OR t.fingerprint LIKE 'iteration-%'
      OR t.fingerprint LIKE 'beta-%')
), updated AS (
  UPDATE pos.cash_shifts cs
  SET status = 'closed',
      closed_by_user_id = c.opened_by_user_id,
      counted_cash_cents = cs.expected_cash_cents,
      difference_cents = 0,
      closed_at = now(),
      updated_at = now()
  FROM candidates c
  WHERE cs.tenant_id = c.tenant_id AND cs.id = c.id
  RETURNING cs.id, cs.tenant_id, cs.terminal_id, cs.opened_by_user_id,
            c.fingerprint, c.opened_at, cs.expected_cash_cents, cs.counted_cash_cents,
            cs.difference_cents, cs.closed_at
), audited AS (
  INSERT INTO pos.audit_events(
    tenant_id, actor_user_id, terminal_id, action, entity_type, entity_id,
    before_data, after_data, trace_id, occurred_at
  )
  SELECT tenant_id, opened_by_user_id, terminal_id,
         'beta09.validation_stale_shift_closed', 'cash_shift', id,
         jsonb_build_object(
           'status','open',
           'openedAt',opened_at,
           'fingerprint',fingerprint
         ),
         jsonb_build_object(
           'status','closed',
           'countedCashCents',counted_cash_cents,
           'expectedCashCents',expected_cash_cents,
           'differenceCents',difference_cents,
           'closedAt',closed_at,
           'reason','stale validation fixture cleanup before BETA-09 data quality closure'
         ),
         'beta-09-hotfix-09-1', now()
  FROM updated
  RETURNING id
)
SELECT json_build_object(
  'closedValidationShiftCount',(SELECT count(*) FROM updated),
  'auditEventCount',(SELECT count(*) FROM audited)
)::text;
