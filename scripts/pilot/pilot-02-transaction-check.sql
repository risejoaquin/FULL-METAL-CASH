\set ON_ERROR_STOP on

SELECT set_config('app.current_tenant_id', :'tenant_id', false);

WITH params AS (
  SELECT
    :'tenant_id'::uuid AS tenant_id,
    :'sale_id'::uuid AS sale_id,
    :'receipt_id'::uuid AS receipt_id,
    :'expected_total_cents'::bigint AS expected_total_cents,
    :'expected_paid_cents'::bigint AS expected_paid_cents,
    :'expected_change_cents'::bigint AS expected_change_cents,
    :'product_sku'::text AS product_sku,
    :'payment_method_code'::text AS payment_method_code
), facts AS (
  SELECT
    p.tenant_id,
    p.sale_id,
    COUNT(*) FILTER (
      WHERE s.id IS NOT NULL
        AND s.status = 'completed'
        AND s.total_cents = p.expected_total_cents
        AND s.paid_cents = p.expected_paid_cents
        AND s.change_cents = p.expected_change_cents
        AND s.deleted_at IS NULL
    ) AS completed_sale_count,
    COUNT(DISTINCT sl.id) AS sale_line_count,
    COUNT(DISTINCT sp.id) FILTER (
      WHERE sp.status = 'approved'
        AND sp.amount_cents = p.expected_paid_cents
    ) AS captured_payment_count,
    COUNT(DISTINCT dr.id) FILTER (
      WHERE dr.status = 'active'
        AND dr.id = p.receipt_id
    ) AS active_receipt_count,
    COUNT(DISTINCT il.id) FILTER (
      WHERE il.reference_type = 'sale'
        AND il.reference_id = p.sale_id
        AND il.quantity_delta < 0
    ) AS sale_inventory_ledger_count,
    COUNT(DISTINCT ae_sale.id) FILTER (
      WHERE ae_sale.action = 'sale.completed'
        AND ae_sale.entity_type = 'sale'
        AND ae_sale.entity_id = p.sale_id
    ) AS sale_audit_count,
    COUNT(DISTINCT ae_receipt.id) FILTER (
      WHERE ae_receipt.action = 'receipt.issued'
        AND ae_receipt.entity_type = 'digital_receipt'
        AND ae_receipt.entity_id = p.receipt_id
    ) AS receipt_audit_count
  FROM params p
  LEFT JOIN pos.sales s
    ON s.tenant_id = p.tenant_id
   AND s.id = p.sale_id
  LEFT JOIN pos.sale_lines sl
    ON sl.tenant_id = p.tenant_id
   AND sl.sale_id = p.sale_id
  LEFT JOIN pos.products pr
    ON pr.tenant_id = p.tenant_id
   AND pr.id = sl.product_id
   AND pr.sku = p.product_sku
  LEFT JOIN pos.payments sp
    ON sp.tenant_id = p.tenant_id
   AND sp.sale_id = p.sale_id
  LEFT JOIN pos.payment_methods pm
    ON pm.tenant_id = p.tenant_id
   AND pm.id = sp.payment_method_id
   AND pm.code = p.payment_method_code
  LEFT JOIN pos.digital_receipts dr
    ON dr.tenant_id = p.tenant_id
   AND dr.sale_id = p.sale_id
  LEFT JOIN pos.inventory_ledger il
    ON il.tenant_id = p.tenant_id
   AND il.reference_id = p.sale_id
  LEFT JOIN pos.audit_events ae_sale
    ON ae_sale.tenant_id = p.tenant_id
   AND ae_sale.entity_id = p.sale_id
  LEFT JOIN pos.audit_events ae_receipt
    ON ae_receipt.tenant_id = p.tenant_id
   AND ae_receipt.entity_id = p.receipt_id
  GROUP BY p.tenant_id, p.sale_id
), verdict AS (
  SELECT
    *,
    CASE
      WHEN completed_sale_count = 1
       AND sale_line_count >= 1
       AND captured_payment_count >= 1
       AND active_receipt_count = 1
       AND sale_inventory_ledger_count >= 1
       AND sale_audit_count >= 1
       AND receipt_audit_count >= 1
      THEN 'GO'
      ELSE 'NO-GO'
    END AS pilot_02_go_no_go
  FROM facts
)
SELECT * FROM verdict;

WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id, :'sale_id'::uuid AS sale_id, :'receipt_id'::uuid AS receipt_id,
         :'expected_total_cents'::bigint AS expected_total_cents,
         :'expected_paid_cents'::bigint AS expected_paid_cents,
         :'expected_change_cents'::bigint AS expected_change_cents,
         :'product_sku'::text AS product_sku,
         :'payment_method_code'::text AS payment_method_code
), facts AS (
  SELECT
    COUNT(*) FILTER (WHERE s.id IS NOT NULL AND s.status = 'completed' AND s.total_cents = p.expected_total_cents AND s.paid_cents = p.expected_paid_cents AND s.change_cents = p.expected_change_cents AND s.deleted_at IS NULL) AS completed_sale_count,
    COUNT(DISTINCT sl.id) AS sale_line_count,
    COUNT(DISTINCT sp.id) FILTER (WHERE sp.status = 'approved' AND sp.amount_cents = p.expected_paid_cents) AS captured_payment_count,
    COUNT(DISTINCT dr.id) FILTER (WHERE dr.status = 'active' AND dr.id = p.receipt_id) AS active_receipt_count,
    COUNT(DISTINCT il.id) FILTER (WHERE il.reference_type = 'sale' AND il.reference_id = p.sale_id AND il.quantity_delta < 0) AS sale_inventory_ledger_count,
    COUNT(DISTINCT ae_sale.id) FILTER (WHERE ae_sale.action = 'sale.completed' AND ae_sale.entity_type = 'sale' AND ae_sale.entity_id = p.sale_id) AS sale_audit_count,
    COUNT(DISTINCT ae_receipt.id) FILTER (WHERE ae_receipt.action = 'receipt.issued' AND ae_receipt.entity_type = 'digital_receipt' AND ae_receipt.entity_id = p.receipt_id) AS receipt_audit_count
  FROM params p
  LEFT JOIN pos.sales s ON s.tenant_id = p.tenant_id AND s.id = p.sale_id
  LEFT JOIN pos.sale_lines sl ON sl.tenant_id = p.tenant_id AND sl.sale_id = p.sale_id
  LEFT JOIN pos.payments sp ON sp.tenant_id = p.tenant_id AND sp.sale_id = p.sale_id
  LEFT JOIN pos.digital_receipts dr ON dr.tenant_id = p.tenant_id AND dr.sale_id = p.sale_id
  LEFT JOIN pos.inventory_ledger il ON il.tenant_id = p.tenant_id AND il.reference_id = p.sale_id
  LEFT JOIN pos.audit_events ae_sale ON ae_sale.tenant_id = p.tenant_id AND ae_sale.entity_id = p.sale_id
  LEFT JOIN pos.audit_events ae_receipt ON ae_receipt.tenant_id = p.tenant_id AND ae_receipt.entity_id = p.receipt_id
), assertion AS (
  SELECT
    CASE
      WHEN completed_sale_count = 1
       AND sale_line_count >= 1
       AND captured_payment_count >= 1
       AND active_receipt_count = 1
       AND sale_inventory_ledger_count >= 1
       AND sale_audit_count >= 1
       AND receipt_audit_count >= 1
      THEN 'PILOT-02 real POS transaction validation PASS'
      ELSE 'PILOT-02 real POS transaction validation NO-GO'
    END AS pilot_02_assertion,
    CASE
      WHEN completed_sale_count = 1
       AND sale_line_count >= 1
       AND captured_payment_count >= 1
       AND active_receipt_count = 1
       AND sale_inventory_ledger_count >= 1
       AND sale_audit_count >= 1
       AND receipt_audit_count >= 1
      THEN 'GO'
      ELSE 'NO-GO'
    END AS pilot_02_go_no_go,
    completed_sale_count,
    sale_line_count,
    captured_payment_count,
    active_receipt_count,
    sale_inventory_ledger_count,
    sale_audit_count,
    receipt_audit_count
  FROM facts
)
SELECT * FROM assertion;
