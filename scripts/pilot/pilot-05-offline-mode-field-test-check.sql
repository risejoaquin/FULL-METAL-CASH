\set ON_ERROR_STOP on

SELECT set_config('app.current_tenant_id', :'tenant_id', false);

WITH params AS (
  SELECT
    :'tenant_id'::uuid AS tenant_id,
    :'store_id'::uuid AS store_id,
    :'terminal_id'::uuid AS terminal_id,
    :'batch_id'::uuid AS batch_id,
    :'local_sale_id'::uuid AS local_sale_id,
    :'sale_id'::uuid AS sale_id,
    :'receipt_id'::uuid AS receipt_id,
    :'remote_shift_id'::uuid AS remote_shift_id,
    :'expected_total_cents'::bigint AS expected_total_cents,
    :'expected_cash_cents'::bigint AS expected_cash_cents,
    :'counted_cash_cents'::bigint AS counted_cash_cents,
    :'difference_cents'::bigint AS difference_cents
), facts AS (
  SELECT
    (SELECT COUNT(*) FROM pos.sync_inbox_events e
      WHERE e.tenant_id = p.tenant_id
        AND e.store_id = p.store_id
        AND e.terminal_id = p.terminal_id
        AND e.batch_id = p.batch_id
        AND e.event_type = 'sale.completed'
        AND e.entity_type = 'sale'
        AND e.schema_version = 4
        AND e.status = 'processed') AS processed_sale_sync_event_count,
    (SELECT COUNT(*) FROM pos.sales s
      WHERE s.tenant_id = p.tenant_id
        AND s.store_id = p.store_id
        AND s.terminal_id = p.terminal_id
        AND s.id = p.sale_id
        AND s.local_sale_id = p.local_sale_id
        AND s.status = 'completed'
        AND s.total_cents = p.expected_total_cents
        AND s.paid_cents = p.expected_total_cents
        AND s.deleted_at IS NULL) AS completed_sale_count,
    (SELECT COUNT(*) FROM pos.payments pay
      JOIN pos.payment_methods pm ON pm.tenant_id = pay.tenant_id AND pm.id = pay.payment_method_id
      WHERE pay.tenant_id = p.tenant_id
        AND pay.sale_id = p.sale_id
        AND pm.code = 'cash'
        AND pay.status = 'approved'
        AND pay.amount_cents = p.expected_total_cents) AS approved_cash_payment_count,
    (SELECT COUNT(*) FROM pos.inventory_ledger il
      WHERE il.tenant_id = p.tenant_id
        AND il.store_id = p.store_id
        AND il.terminal_id = p.terminal_id
        AND il.reference_type = 'sale'
        AND il.reference_id = p.sale_id
        AND il.quantity_delta < 0) AS sale_inventory_ledger_count,
    (SELECT COUNT(*) FROM pos.digital_receipts dr
      WHERE dr.tenant_id = p.tenant_id
        AND dr.id = p.receipt_id
        AND dr.sale_id = p.sale_id
        AND dr.status = 'active') AS active_receipt_count,
    (SELECT COUNT(*) FROM pos.sync_changes sc
      WHERE sc.tenant_id = p.tenant_id
        AND sc.store_id = p.store_id
        AND sc.source_terminal_id = p.terminal_id
        AND sc.entity_id = p.sale_id
        AND sc.entity_type = 'sale'
        AND sc.operation IN ('create','update')) AS sale_sync_change_count,
    (SELECT COUNT(*) FROM pos.cash_shifts cs
      WHERE cs.tenant_id = p.tenant_id
        AND cs.id = p.remote_shift_id
        AND cs.store_id = p.store_id
        AND cs.terminal_id = p.terminal_id
        AND cs.status = 'closed'
        AND cs.expected_cash_cents = p.expected_cash_cents
        AND cs.counted_cash_cents = p.counted_cash_cents
        AND cs.difference_cents = p.difference_cents) AS closed_remote_shift_count,
    (SELECT COUNT(*) FROM pos.audit_events ae
      WHERE ae.tenant_id = p.tenant_id
        AND ae.action IN ('sync.process.completed','sale.created','receipt.issued')
        AND (ae.entity_id = p.batch_id OR ae.entity_id = p.sale_id OR ae.entity_id = p.receipt_id)) AS pilot_audit_count
  FROM params p
), final AS (
  SELECT
    CASE
      WHEN processed_sale_sync_event_count = 1
       AND completed_sale_count = 1
       AND approved_cash_payment_count = 1
       AND sale_inventory_ledger_count >= 1
       AND active_receipt_count = 1
       AND closed_remote_shift_count = 1
       AND pilot_audit_count >= 2
      THEN 'PILOT-05 offline mode field test PASS'
      ELSE 'PILOT-05 offline mode field test NO-GO'
    END AS pilot_05_assertion,
    CASE
      WHEN processed_sale_sync_event_count = 1
       AND completed_sale_count = 1
       AND approved_cash_payment_count = 1
       AND sale_inventory_ledger_count >= 1
       AND active_receipt_count = 1
       AND closed_remote_shift_count = 1
       AND pilot_audit_count >= 2
      THEN 'GO'
      ELSE 'NO-GO'
    END AS pilot_05_go_no_go,
    processed_sale_sync_event_count,
    completed_sale_count,
    approved_cash_payment_count,
    sale_inventory_ledger_count,
    active_receipt_count,
    sale_sync_change_count,
    closed_remote_shift_count,
    pilot_audit_count
  FROM facts
)
SELECT * FROM final;
