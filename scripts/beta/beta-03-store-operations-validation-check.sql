\set ON_ERROR_STOP on
SELECT set_config('app.current_tenant_id', :'tenant_id', false);
WITH p AS (
  SELECT :'tenant_id'::uuid tenant_id, :'shift_id'::uuid shift_id, :'sale_id'::uuid sale_id, :'receipt_id'::uuid receipt_id,
         :'expected_cash_cents'::bigint expected_cash_cents, :'counted_cash_cents'::bigint counted_cash_cents,
         :'difference_cents'::bigint difference_cents
), f AS (
  SELECT
    (SELECT count(*) FROM pos.cash_shifts cs WHERE cs.tenant_id=p.tenant_id AND cs.id=p.shift_id AND cs.status='closed' AND cs.expected_cash_cents=p.expected_cash_cents AND cs.counted_cash_cents=p.counted_cash_cents AND cs.difference_cents=p.difference_cents) closed_shift_count,
    (SELECT count(*) FROM pos.sales s WHERE s.tenant_id=p.tenant_id AND s.id=p.sale_id AND s.cash_shift_id=p.shift_id AND s.status='completed') completed_sale_count,
    (SELECT count(*) FROM pos.payments pay JOIN pos.sales s ON s.tenant_id=pay.tenant_id AND s.id=pay.sale_id WHERE s.tenant_id=p.tenant_id AND s.id=p.sale_id AND pay.status='approved') approved_payment_count,
    (SELECT count(*) FROM pos.digital_receipts dr WHERE dr.tenant_id=p.tenant_id AND dr.id=p.receipt_id AND dr.sale_id=p.sale_id AND dr.status='active') active_receipt_count,
    (SELECT count(*) FROM pos.cash_movements cm WHERE cm.tenant_id=p.tenant_id AND cm.cash_shift_id=p.shift_id AND cm.movement_type='cash_in') cash_in_count,
    (SELECT count(*) FROM pos.cash_movements cm WHERE cm.tenant_id=p.tenant_id AND cm.cash_shift_id=p.shift_id AND cm.movement_type='cash_out') cash_out_count,
    (SELECT count(*) FROM pos.cash_movements cm WHERE cm.tenant_id=p.tenant_id AND cm.cash_shift_id=p.shift_id AND cm.movement_type='drawer_open_no_sale') drawer_open_count,
    (SELECT count(*) FROM pos.audit_events ae WHERE ae.tenant_id=p.tenant_id AND ae.action='cash.shift.opened' AND ae.entity_type='cash_shift' AND ae.entity_id=p.shift_id) shift_open_audit_count,
    (SELECT count(*) FROM pos.audit_events ae WHERE ae.tenant_id=p.tenant_id AND ae.action='cash.shift.closed' AND ae.entity_type='cash_shift' AND ae.entity_id=p.shift_id) shift_close_audit_count,
    (SELECT count(*) FROM pos.audit_events ae WHERE ae.tenant_id=p.tenant_id AND ae.action='receipt.issued' AND ae.entity_type='digital_receipt' AND ae.entity_id=p.receipt_id) receipt_audit_count
  FROM p
), v AS (
  SELECT *, CASE WHEN closed_shift_count=1 AND completed_sale_count=1 AND approved_payment_count>=1 AND active_receipt_count=1
    AND cash_in_count>=1 AND cash_out_count>=1 AND drawer_open_count>=1 AND shift_open_audit_count>=1 AND shift_close_audit_count>=1
    AND receipt_audit_count>=1 AND (SELECT difference_cents FROM p)=0 THEN 'GO' ELSE 'NO-GO' END decision FROM f
)
SELECT json_build_object(
  'closedShiftCount',closed_shift_count,'completedSaleCount',completed_sale_count,'approvedPaymentCount',approved_payment_count,
  'activeReceiptCount',active_receipt_count,'cashInCount',cash_in_count,'cashOutCount',cash_out_count,'drawerOpenCount',drawer_open_count,
  'shiftOpenAuditCount',shift_open_audit_count,'shiftCloseAuditCount',shift_close_audit_count,'receiptAuditCount',receipt_audit_count,
  'differenceCents',(SELECT difference_cents FROM p),'blockers',CASE WHEN decision='GO' THEN '[]'::json ELSE json_build_array('store_operations_reconciliation_failed') END,
  'decision',decision
)::text FROM v;
