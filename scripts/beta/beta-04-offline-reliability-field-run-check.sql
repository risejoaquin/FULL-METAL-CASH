\set ON_ERROR_STOP on

SELECT set_config('app.current_tenant_id', :'tenant_id', false);

WITH params AS (
  SELECT
    :'tenant_id'::uuid AS tenant_id,
    :'store_id'::uuid AS store_id,
    :'terminal_id'::uuid AS terminal_id,
    :'local_sale_id'::uuid AS local_sale_id,
    :'sale_id'::uuid AS sale_id,
    :'batch_id'::uuid AS batch_id,
    :'baseline_dead_letter_count'::bigint AS baseline_dead_letter_count,
    :'baseline_pending_conflict_count'::bigint AS baseline_pending_conflict_count
), facts AS (
  SELECT
    (SELECT COUNT(*) FROM pos.sales s
      WHERE s.tenant_id = p.tenant_id
        AND s.store_id = p.store_id
        AND s.terminal_id = p.terminal_id
        AND s.local_sale_id = p.local_sale_id
        AND s.status = 'completed'
        AND s.deleted_at IS NULL) AS sale_count,
    (SELECT COUNT(*) FROM pos.payments pay
      JOIN pos.payment_methods pm ON pm.tenant_id = pay.tenant_id AND pm.id = pay.payment_method_id
      WHERE pay.tenant_id = p.tenant_id
        AND pay.sale_id = p.sale_id
        AND pay.status = 'approved'
        AND pm.code = 'cash') AS approved_cash_payment_count,
    (SELECT COUNT(*) FROM pos.sync_inbox_events e
      WHERE e.tenant_id = p.tenant_id
        AND e.store_id = p.store_id
        AND e.terminal_id = p.terminal_id
        AND e.event_type = 'sale.completed'
        AND e.entity_type = 'sale'
        AND e.entity_id = p.local_sale_id
        AND e.schema_version = 4
        AND e.status = 'processed') AS processed_sale_event_count,
    (SELECT COUNT(*) FROM pos.sync_inbox_events e
      WHERE e.tenant_id = p.tenant_id
        AND e.status = 'dead_letter') AS final_dead_letter_count,
    (SELECT COUNT(*) FROM pos.sync_conflicts c
      WHERE c.tenant_id = p.tenant_id
        AND c.status = 'pending') AS final_pending_conflict_count,
    (SELECT COUNT(*) FROM pos.sync_inbox_events e
      WHERE e.tenant_id = p.tenant_id
        AND e.schema_version <> 4) AS legacy_schema_event_count,
    p.baseline_dead_letter_count,
    p.baseline_pending_conflict_count
  FROM params p
), evaluated AS (
  SELECT *,
    CASE WHEN final_dead_letter_count > baseline_dead_letter_count THEN final_dead_letter_count - baseline_dead_letter_count ELSE 0 END AS new_dead_letter_count,
    CASE WHEN final_pending_conflict_count > baseline_pending_conflict_count THEN final_pending_conflict_count - baseline_pending_conflict_count ELSE 0 END AS new_pending_conflict_count
  FROM facts
), result AS (
  SELECT
    *,
    ARRAY_REMOVE(ARRAY[
      CASE WHEN sale_count <> 1 THEN 'duplicate_or_missing_sale' END,
      CASE WHEN approved_cash_payment_count <> 1 THEN 'duplicate_or_missing_payment' END,
      CASE WHEN processed_sale_event_count <> 1 THEN 'duplicate_or_missing_processed_sale_event' END,
      CASE WHEN new_dead_letter_count <> 0 THEN 'new_dead_letter_created' END,
      CASE WHEN new_pending_conflict_count <> 0 THEN 'new_pending_conflict_created' END,
      CASE WHEN legacy_schema_event_count <> 0 THEN 'legacy_schema_event_detected' END
    ], NULL) AS blockers
  FROM evaluated
)
SELECT json_build_object(
  'decision', CASE WHEN cardinality(blockers) = 0 THEN 'GO' ELSE 'NO-GO' END,
  'saleCount', sale_count,
  'approvedCashPaymentCount', approved_cash_payment_count,
  'processedSaleEventCount', processed_sale_event_count,
  'baselineDeadLetterCount', baseline_dead_letter_count,
  'finalDeadLetterCount', final_dead_letter_count,
  'newDeadLetterCount', new_dead_letter_count,
  'baselinePendingConflictCount', baseline_pending_conflict_count,
  'finalPendingConflictCount', final_pending_conflict_count,
  'newPendingConflictCount', new_pending_conflict_count,
  'legacySchemaEventCount', legacy_schema_event_count,
  'blockers', blockers
)::text
FROM result;
