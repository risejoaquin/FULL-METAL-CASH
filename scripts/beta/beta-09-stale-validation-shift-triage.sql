\set ON_ERROR_STOP on
WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id, now() AS checked_at
), stale AS (
  SELECT cs.id, cs.terminal_id, cs.opened_by_user_id, cs.expected_cash_cents, cs.opened_at,
         t.fingerprint, t.name, t.app_version,
         (t.fingerprint LIKE 'pilot-%'
          OR t.fingerprint LIKE 'exp-%'
          OR t.fingerprint LIKE 'iteration-%'
          OR t.fingerprint LIKE 'beta-%') AS validation_owned
  FROM pos.cash_shifts cs
  JOIN params p ON p.tenant_id = cs.tenant_id
  JOIN pos.terminals t ON t.tenant_id = cs.tenant_id AND t.id = cs.terminal_id
  WHERE lower(coalesce(cs.status,'')) = 'open'
    AND cs.opened_at < p.checked_at - interval '24 hours'
), facts AS (
  SELECT
    count(*)::bigint AS stale_open_shift_count,
    count(*) FILTER (WHERE validation_owned)::bigint AS validation_owned_stale_shift_count,
    count(*) FILTER (WHERE NOT validation_owned)::bigint AS non_validation_stale_shift_count,
    coalesce(json_agg(json_build_object(
      'shiftId', id,
      'terminalId', terminal_id,
      'fingerprint', fingerprint,
      'terminalName', name,
      'appVersion', app_version,
      'openedAt', opened_at,
      'expectedCashCents', expected_cash_cents,
      'validationOwned', validation_owned
    ) ORDER BY opened_at) FILTER (WHERE true), '[]'::json) AS stale_shifts
  FROM stale
)
SELECT json_build_object(
  'triageDecision', CASE WHEN non_validation_stale_shift_count = 0 THEN 'GO_VALIDATION_FIXTURE_CLEANUP' ELSE 'NO_GO_REAL_OPERATOR_SHIFT_REVIEW_REQUIRED' END,
  'staleOpenShiftCount', stale_open_shift_count,
  'validationOwnedStaleShiftCount', validation_owned_stale_shift_count,
  'nonValidationStaleShiftCount', non_validation_stale_shift_count,
  'staleShifts', stale_shifts
)::text
FROM facts;
