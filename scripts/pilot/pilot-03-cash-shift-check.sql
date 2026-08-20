\set ON_ERROR_STOP on

SELECT set_config('app.current_tenant_id', :'tenant_id', false);

WITH params AS (
  SELECT
    :'tenant_id'::uuid AS tenant_id,
    :'shift_id'::uuid AS shift_id,
    :'terminal_id'::uuid AS terminal_id,
    :'store_id'::uuid AS store_id,
    :'sale_1_id'::uuid AS sale_1_id,
    :'sale_2_id'::uuid AS sale_2_id,
    :'cash_in_id'::uuid AS cash_in_id,
    :'cash_out_id'::uuid AS cash_out_id,
    :'drawer_open_id'::uuid AS drawer_open_id,
    :'opening_amount_cents'::bigint AS opening_amount_cents,
    :'cash_in_cents'::bigint AS cash_in_cents,
    :'cash_out_cents'::bigint AS cash_out_cents,
    :'cash_sales_cents'::bigint AS cash_sales_cents,
    :'expected_cash_cents'::bigint AS expected_cash_cents,
    :'counted_cash_cents'::bigint AS counted_cash_cents,
    :'difference_cents'::bigint AS difference_cents
), facts AS (
  SELECT
    p.tenant_id,
    p.shift_id,
    (
      SELECT COUNT(*)
      FROM pos.cash_shifts cs
      WHERE cs.tenant_id = p.tenant_id
        AND cs.id = p.shift_id
        AND cs.status = 'closed'
        AND cs.store_id = p.store_id
        AND cs.terminal_id = p.terminal_id
        AND cs.opening_amount_cents = p.opening_amount_cents
        AND cs.expected_cash_cents = p.expected_cash_cents
        AND cs.counted_cash_cents = p.counted_cash_cents
        AND cs.difference_cents = p.difference_cents
        AND cs.closed_at IS NOT NULL
    ) AS closed_shift_count,
    (
      SELECT COUNT(DISTINCT cm.id)
      FROM pos.cash_movements cm
      WHERE cm.tenant_id = p.tenant_id
        AND cm.cash_shift_id = p.shift_id
        AND cm.id IN (p.cash_in_id, p.cash_out_id, p.drawer_open_id)
    ) AS cash_movement_count,
    (
      SELECT COUNT(*)
      FROM pos.cash_movements cm
      WHERE cm.tenant_id = p.tenant_id
        AND cm.cash_shift_id = p.shift_id
        AND cm.id = p.cash_in_id
        AND cm.movement_type = 'cash_in'
        AND cm.amount_cents = p.cash_in_cents
    ) AS cash_in_count,
    (
      SELECT COUNT(*)
      FROM pos.cash_movements cm
      WHERE cm.tenant_id = p.tenant_id
        AND cm.cash_shift_id = p.shift_id
        AND cm.id = p.cash_out_id
        AND cm.movement_type = 'cash_out'
        AND cm.amount_cents = p.cash_out_cents
    ) AS cash_out_count,
    (
      SELECT COUNT(*)
      FROM pos.cash_movements cm
      WHERE cm.tenant_id = p.tenant_id
        AND cm.cash_shift_id = p.shift_id
        AND cm.id = p.drawer_open_id
        AND cm.movement_type = 'drawer_open_no_sale'
        AND cm.amount_cents = 0
    ) AS drawer_open_count,
    (
      SELECT COUNT(DISTINCT s.id)
      FROM pos.sales s
      WHERE s.tenant_id = p.tenant_id
        AND s.cash_shift_id = p.shift_id
        AND s.id IN (p.sale_1_id, p.sale_2_id)
        AND s.status = 'completed'
    ) AS completed_sale_count,
    (
      SELECT COALESCE(SUM(pay.amount_cents), 0)::bigint
      FROM pos.sales s
      JOIN pos.payments pay ON pay.tenant_id = s.tenant_id AND pay.sale_id = s.id
      JOIN pos.payment_methods pm ON pm.tenant_id = pay.tenant_id AND pm.id = pay.payment_method_id
      WHERE s.tenant_id = p.tenant_id
        AND s.cash_shift_id = p.shift_id
        AND s.id IN (p.sale_1_id, p.sale_2_id)
        AND s.status = 'completed'
        AND pay.status = 'approved'
        AND pm.method_type = 'cash'
    ) AS approved_cash_payment_cents,
    (
      SELECT COUNT(DISTINCT ae.id)
      FROM pos.audit_events ae
      WHERE ae.tenant_id = p.tenant_id
        AND ae.action = 'cash.shift.opened'
        AND ae.entity_type = 'cash_shift'
        AND ae.entity_id = p.shift_id
    ) AS shift_open_audit_count,
    (
      SELECT COUNT(DISTINCT ae.id)
      FROM pos.audit_events ae
      WHERE ae.tenant_id = p.tenant_id
        AND ae.action = 'cash.shift.closed'
        AND ae.entity_type = 'cash_shift'
        AND ae.entity_id = p.shift_id
    ) AS shift_close_audit_count,
    (
      SELECT COUNT(DISTINCT ae.id)
      FROM pos.audit_events ae
      WHERE ae.tenant_id = p.tenant_id
        AND ae.action = 'cash.movement.created'
        AND ae.entity_type = 'cash_movement'
        AND ae.entity_id IN (p.cash_in_id, p.cash_out_id, p.drawer_open_id)
    ) AS movement_audit_count
  FROM params p
), assertion AS (
  SELECT
    *,
    CASE
      WHEN closed_shift_count = 1
       AND cash_movement_count = 3
       AND cash_in_count = 1
       AND cash_out_count = 1
       AND drawer_open_count = 1
       AND completed_sale_count = 2
       AND approved_cash_payment_cents = (SELECT cash_sales_cents FROM params)
       AND shift_open_audit_count >= 1
       AND shift_close_audit_count >= 1
       AND movement_audit_count >= 3
      THEN 'GO'
      ELSE 'NO-GO'
    END AS pilot_03_go_no_go
  FROM facts
)
SELECT * FROM assertion;

WITH params AS (
  SELECT
    :'tenant_id'::uuid AS tenant_id,
    :'shift_id'::uuid AS shift_id,
    :'sale_1_id'::uuid AS sale_1_id,
    :'sale_2_id'::uuid AS sale_2_id,
    :'cash_in_id'::uuid AS cash_in_id,
    :'cash_out_id'::uuid AS cash_out_id,
    :'drawer_open_id'::uuid AS drawer_open_id,
    :'cash_sales_cents'::bigint AS cash_sales_cents
), facts AS (
  SELECT
    (
      SELECT COUNT(*) FROM pos.cash_shifts cs
      WHERE cs.tenant_id = p.tenant_id AND cs.id = p.shift_id AND cs.status = 'closed' AND cs.difference_cents = 0
    ) AS closed_shift_count,
    (
      SELECT COUNT(DISTINCT cm.id) FROM pos.cash_movements cm
      WHERE cm.tenant_id = p.tenant_id AND cm.cash_shift_id = p.shift_id AND cm.id IN (p.cash_in_id, p.cash_out_id, p.drawer_open_id)
    ) AS cash_movement_count,
    (
      SELECT COUNT(*) FROM pos.cash_movements cm
      WHERE cm.tenant_id = p.tenant_id AND cm.cash_shift_id = p.shift_id AND cm.id = p.cash_in_id AND cm.movement_type = 'cash_in'
    ) AS cash_in_count,
    (
      SELECT COUNT(*) FROM pos.cash_movements cm
      WHERE cm.tenant_id = p.tenant_id AND cm.cash_shift_id = p.shift_id AND cm.id = p.cash_out_id AND cm.movement_type = 'cash_out'
    ) AS cash_out_count,
    (
      SELECT COUNT(*) FROM pos.cash_movements cm
      WHERE cm.tenant_id = p.tenant_id AND cm.cash_shift_id = p.shift_id AND cm.id = p.drawer_open_id AND cm.movement_type = 'drawer_open_no_sale'
    ) AS drawer_open_count,
    (
      SELECT COUNT(DISTINCT s.id) FROM pos.sales s
      WHERE s.tenant_id = p.tenant_id AND s.cash_shift_id = p.shift_id AND s.id IN (p.sale_1_id, p.sale_2_id) AND s.status = 'completed'
    ) AS completed_sale_count,
    (
      SELECT COALESCE(SUM(pay.amount_cents), 0)::bigint
      FROM pos.sales s
      JOIN pos.payments pay ON pay.tenant_id = s.tenant_id AND pay.sale_id = s.id
      JOIN pos.payment_methods pm ON pm.tenant_id = pay.tenant_id AND pm.id = pay.payment_method_id
      WHERE s.tenant_id = p.tenant_id AND s.cash_shift_id = p.shift_id AND s.id IN (p.sale_1_id, p.sale_2_id)
        AND s.status = 'completed' AND pay.status = 'approved' AND pm.method_type = 'cash'
    ) AS approved_cash_payment_cents,
    (
      SELECT COUNT(DISTINCT ae.id) FROM pos.audit_events ae
      WHERE ae.tenant_id = p.tenant_id AND ae.action = 'cash.shift.opened' AND ae.entity_type = 'cash_shift' AND ae.entity_id = p.shift_id
    ) AS shift_open_audit_count,
    (
      SELECT COUNT(DISTINCT ae.id) FROM pos.audit_events ae
      WHERE ae.tenant_id = p.tenant_id AND ae.action = 'cash.shift.closed' AND ae.entity_type = 'cash_shift' AND ae.entity_id = p.shift_id
    ) AS shift_close_audit_count,
    (
      SELECT COUNT(DISTINCT ae.id) FROM pos.audit_events ae
      WHERE ae.tenant_id = p.tenant_id AND ae.action = 'cash.movement.created' AND ae.entity_type = 'cash_movement' AND ae.entity_id IN (p.cash_in_id, p.cash_out_id, p.drawer_open_id)
    ) AS movement_audit_count
  FROM params p
), final AS (
  SELECT
    CASE
      WHEN closed_shift_count = 1
       AND cash_movement_count = 3
       AND cash_in_count = 1
       AND cash_out_count = 1
       AND drawer_open_count = 1
       AND completed_sale_count = 2
       AND approved_cash_payment_cents = (SELECT cash_sales_cents FROM params)
       AND shift_open_audit_count >= 1
       AND shift_close_audit_count >= 1
       AND movement_audit_count >= 3
      THEN 'PILOT-03 cash drawer and shift operations validation PASS'
      ELSE 'PILOT-03 cash drawer and shift operations validation NO-GO'
    END AS pilot_03_assertion,
    CASE
      WHEN closed_shift_count = 1
       AND cash_movement_count = 3
       AND cash_in_count = 1
       AND cash_out_count = 1
       AND drawer_open_count = 1
       AND completed_sale_count = 2
       AND approved_cash_payment_cents = (SELECT cash_sales_cents FROM params)
       AND shift_open_audit_count >= 1
       AND shift_close_audit_count >= 1
       AND movement_audit_count >= 3
      THEN 'GO'
      ELSE 'NO-GO'
    END AS pilot_03_go_no_go,
    closed_shift_count,
    cash_movement_count,
    cash_in_count,
    cash_out_count,
    drawer_open_count,
    completed_sale_count,
    approved_cash_payment_cents,
    shift_open_audit_count,
    shift_close_audit_count,
    movement_audit_count
  FROM facts
)
SELECT * FROM final;
