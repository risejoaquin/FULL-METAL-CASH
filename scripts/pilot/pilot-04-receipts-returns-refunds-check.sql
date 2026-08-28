\set ON_ERROR_STOP on

SELECT set_config('app.current_tenant_id', :'tenant_id', false);

WITH params AS (
  SELECT
    :'tenant_id'::uuid AS tenant_id,
    :'shift_id'::uuid AS shift_id,
    :'sale_id'::uuid AS sale_id,
    :'sale_line_id'::uuid AS sale_line_id,
    :'return_id'::uuid AS return_id,
    :'receipt_id'::uuid AS receipt_id,
    :'expected_total_cents'::bigint AS expected_total_cents,
    :'expected_refund_cents'::bigint AS expected_refund_cents,
    :'expected_cash_cents'::bigint AS expected_cash_cents,
    :'counted_cash_cents'::bigint AS counted_cash_cents,
    :'difference_cents'::bigint AS difference_cents,
    :'payment_method_code'::text AS payment_method_code
), facts AS (
  SELECT
    p.tenant_id,
    p.shift_id,
    p.sale_id,
    p.return_id,
    COUNT(*) FILTER (
      WHERE s.id IS NOT NULL
        AND s.status = 'returned'
        AND s.total_cents = p.expected_total_cents
        AND s.deleted_at IS NULL
    ) AS returned_sale_count,
    COUNT(DISTINCT dr.id) FILTER (
      WHERE dr.id = p.receipt_id
        AND dr.sale_id = p.sale_id
        AND dr.status = 'active'
    ) AS active_receipt_count,
    COUNT(DISTINCT r.id) FILTER (
      WHERE r.id = p.return_id
        AND r.sale_id = p.sale_id
        AND r.status = 'completed'
        AND r.total_cents = p.expected_refund_cents
        AND r.refund_cents = p.expected_refund_cents
    ) AS completed_return_count,
    COUNT(DISTINCT rl.id) FILTER (
      WHERE rl.return_id = p.return_id
        AND rl.sale_line_id = p.sale_line_id
        AND rl.total_cents = p.expected_refund_cents
    ) AS return_line_count,
    COUNT(DISTINCT rr.id) FILTER (
      WHERE rr.return_id = p.return_id
        AND rr.method_code = p.payment_method_code
        AND rr.method_type = 'cash'
        AND rr.status = 'approved'
        AND rr.amount_cents = p.expected_refund_cents
    ) AS approved_refund_count,
    COUNT(DISTINCT il_return.id) FILTER (
      WHERE il_return.reference_type = 'return'
        AND il_return.reference_id = p.return_id
        AND il_return.quantity_delta > 0
    ) AS return_inventory_ledger_count,
    COUNT(DISTINCT cm.id) FILTER (
      WHERE cm.cash_shift_id = p.shift_id
        AND cm.movement_type = 'cash_out'
        AND cm.amount_cents = p.expected_refund_cents
        AND cm.reason ILIKE ('Return refund ' || p.return_id::text || ':%')
    ) AS cash_refund_movement_count,
    COUNT(DISTINCT cs.id) FILTER (
      WHERE cs.id = p.shift_id
        AND cs.status = 'closed'
        AND cs.expected_cash_cents = p.expected_cash_cents
        AND cs.counted_cash_cents = p.counted_cash_cents
        AND cs.difference_cents = p.difference_cents
    ) AS closed_shift_count,
    COUNT(DISTINCT ae_receipt.id) FILTER (
      WHERE ae_receipt.action = 'receipt.issued'
        AND ae_receipt.entity_type = 'digital_receipt'
        AND ae_receipt.entity_id = p.receipt_id
    ) AS receipt_audit_count,
    COUNT(DISTINCT ae_return.id) FILTER (
      WHERE ae_return.action = 'return.created'
        AND ae_return.entity_type = 'return'
        AND ae_return.entity_id = p.return_id
    ) AS return_audit_count,
    COUNT(DISTINCT ae_email.id) FILTER (
      WHERE ae_email.action = 'receipt.email_stub_queued'
        AND ae_email.entity_type = 'digital_receipt'
        AND ae_email.entity_id = p.receipt_id
    ) AS receipt_email_audit_count
  FROM params p
  LEFT JOIN pos.sales s ON s.tenant_id = p.tenant_id AND s.id = p.sale_id
  LEFT JOIN pos.digital_receipts dr ON dr.tenant_id = p.tenant_id AND dr.id = p.receipt_id
  LEFT JOIN pos.returns r ON r.tenant_id = p.tenant_id AND r.id = p.return_id
  LEFT JOIN pos.return_lines rl ON rl.tenant_id = p.tenant_id AND rl.return_id = p.return_id
  LEFT JOIN pos.return_refunds rr ON rr.tenant_id = p.tenant_id AND rr.return_id = p.return_id
  LEFT JOIN pos.inventory_ledger il_return ON il_return.tenant_id = p.tenant_id AND il_return.reference_id = p.return_id
  LEFT JOIN pos.cash_movements cm ON cm.tenant_id = p.tenant_id AND cm.cash_shift_id = p.shift_id
  LEFT JOIN pos.cash_shifts cs ON cs.tenant_id = p.tenant_id AND cs.id = p.shift_id
  LEFT JOIN pos.audit_events ae_receipt ON ae_receipt.tenant_id = p.tenant_id AND ae_receipt.entity_id = p.receipt_id
  LEFT JOIN pos.audit_events ae_return ON ae_return.tenant_id = p.tenant_id AND ae_return.entity_id = p.return_id
  LEFT JOIN pos.audit_events ae_email ON ae_email.tenant_id = p.tenant_id AND ae_email.entity_id = p.receipt_id
  GROUP BY p.tenant_id, p.shift_id, p.sale_id, p.return_id
), verdict AS (
  SELECT
    *,
    CASE
      WHEN returned_sale_count = 1
       AND active_receipt_count = 1
       AND completed_return_count = 1
       AND return_line_count >= 1
       AND approved_refund_count = 1
       AND return_inventory_ledger_count >= 1
       AND cash_refund_movement_count >= 1
       AND closed_shift_count = 1
       AND receipt_audit_count >= 1
       AND return_audit_count >= 1
       AND receipt_email_audit_count >= 1
      THEN 'GO'
      ELSE 'NO-GO'
    END AS pilot_04_go_no_go
  FROM facts
)
SELECT * FROM verdict;

WITH params AS (
  SELECT
    :'tenant_id'::uuid AS tenant_id,
    :'shift_id'::uuid AS shift_id,
    :'sale_id'::uuid AS sale_id,
    :'sale_line_id'::uuid AS sale_line_id,
    :'return_id'::uuid AS return_id,
    :'receipt_id'::uuid AS receipt_id,
    :'expected_total_cents'::bigint AS expected_total_cents,
    :'expected_refund_cents'::bigint AS expected_refund_cents,
    :'expected_cash_cents'::bigint AS expected_cash_cents,
    :'counted_cash_cents'::bigint AS counted_cash_cents,
    :'difference_cents'::bigint AS difference_cents,
    :'payment_method_code'::text AS payment_method_code
), facts AS (
  SELECT
    (SELECT COUNT(*) FROM pos.sales s WHERE s.tenant_id = p.tenant_id AND s.id = p.sale_id AND s.status = 'returned' AND s.total_cents = p.expected_total_cents) AS returned_sale_count,
    (SELECT COUNT(*) FROM pos.digital_receipts dr WHERE dr.tenant_id = p.tenant_id AND dr.id = p.receipt_id AND dr.sale_id = p.sale_id AND dr.status = 'active') AS active_receipt_count,
    (SELECT COUNT(*) FROM pos.returns r WHERE r.tenant_id = p.tenant_id AND r.id = p.return_id AND r.sale_id = p.sale_id AND r.status = 'completed' AND r.total_cents = p.expected_refund_cents AND r.refund_cents = p.expected_refund_cents) AS completed_return_count,
    (SELECT COUNT(*) FROM pos.return_lines rl WHERE rl.tenant_id = p.tenant_id AND rl.return_id = p.return_id AND rl.sale_line_id = p.sale_line_id AND rl.total_cents = p.expected_refund_cents) AS return_line_count,
    (SELECT COUNT(*) FROM pos.return_refunds rr WHERE rr.tenant_id = p.tenant_id AND rr.return_id = p.return_id AND rr.method_code = p.payment_method_code AND rr.method_type = 'cash' AND rr.status = 'approved' AND rr.amount_cents = p.expected_refund_cents) AS approved_refund_count,
    (SELECT COUNT(*) FROM pos.inventory_ledger il WHERE il.tenant_id = p.tenant_id AND il.reference_type = 'return' AND il.reference_id = p.return_id AND il.quantity_delta > 0) AS return_inventory_ledger_count,
    (SELECT COUNT(*) FROM pos.cash_movements cm WHERE cm.tenant_id = p.tenant_id AND cm.cash_shift_id = p.shift_id AND cm.movement_type = 'cash_out' AND cm.amount_cents = p.expected_refund_cents AND cm.reason ILIKE ('Return refund ' || p.return_id::text || ':%')) AS cash_refund_movement_count,
    (SELECT COUNT(*) FROM pos.cash_shifts cs WHERE cs.tenant_id = p.tenant_id AND cs.id = p.shift_id AND cs.status = 'closed' AND cs.expected_cash_cents = p.expected_cash_cents AND cs.counted_cash_cents = p.counted_cash_cents AND cs.difference_cents = p.difference_cents) AS closed_shift_count,
    (SELECT COUNT(*) FROM pos.audit_events ae WHERE ae.tenant_id = p.tenant_id AND ae.action = 'receipt.issued' AND ae.entity_type = 'digital_receipt' AND ae.entity_id = p.receipt_id) AS receipt_audit_count,
    (SELECT COUNT(*) FROM pos.audit_events ae WHERE ae.tenant_id = p.tenant_id AND ae.action = 'return.created' AND ae.entity_type = 'return' AND ae.entity_id = p.return_id) AS return_audit_count,
    (SELECT COUNT(*) FROM pos.audit_events ae WHERE ae.tenant_id = p.tenant_id AND ae.action = 'receipt.email_stub_queued' AND ae.entity_type = 'digital_receipt' AND ae.entity_id = p.receipt_id) AS receipt_email_audit_count
  FROM params p
), final AS (
  SELECT
    CASE
      WHEN returned_sale_count = 1
       AND active_receipt_count = 1
       AND completed_return_count = 1
       AND return_line_count >= 1
       AND approved_refund_count = 1
       AND return_inventory_ledger_count >= 1
       AND cash_refund_movement_count >= 1
       AND closed_shift_count = 1
       AND receipt_audit_count >= 1
       AND return_audit_count >= 1
       AND receipt_email_audit_count >= 1
      THEN 'PILOT-04 receipts returns refunds validation PASS'
      ELSE 'PILOT-04 receipts returns refunds validation NO-GO'
    END AS pilot_04_assertion,
    CASE
      WHEN returned_sale_count = 1
       AND active_receipt_count = 1
       AND completed_return_count = 1
       AND return_line_count >= 1
       AND approved_refund_count = 1
       AND return_inventory_ledger_count >= 1
       AND cash_refund_movement_count >= 1
       AND closed_shift_count = 1
       AND receipt_audit_count >= 1
       AND return_audit_count >= 1
       AND receipt_email_audit_count >= 1
      THEN 'GO'
      ELSE 'NO-GO'
    END AS pilot_04_go_no_go,
    returned_sale_count,
    active_receipt_count,
    completed_return_count,
    return_line_count,
    approved_refund_count,
    return_inventory_ledger_count,
    cash_refund_movement_count,
    closed_shift_count,
    receipt_audit_count,
    return_audit_count,
    receipt_email_audit_count
  FROM facts
)
SELECT * FROM final;
