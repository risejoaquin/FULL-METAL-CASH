WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         :'admin_email'::text AS admin_email,
         :'apply_reconciliation'::boolean AS apply_reconciliation,
         gen_random_uuid() AS run_id,
         now() AS occurred_at
), tables AS (
  SELECT to_regclass('pos.tenants') IS NOT NULL AS has_tenants,
         to_regclass('pos.stores') IS NOT NULL AS has_stores,
         to_regclass('pos.terminals') IS NOT NULL AS has_terminals,
         to_regclass('pos.users') IS NOT NULL AS has_users,
         to_regclass('pos.products') IS NOT NULL AS has_products,
         to_regclass('pos.units') IS NOT NULL AS has_units,
         to_regclass('pos.recipes') IS NOT NULL AS has_recipes,
         to_regclass('pos.recipe_items') IS NOT NULL AS has_recipe_items,
         to_regclass('pos.modifiers') IS NOT NULL AS has_modifiers,
         to_regclass('pos.inventory_ledger') IS NOT NULL AS has_inventory_ledger,
         to_regclass('pos.inventory_counts') IS NOT NULL AS has_inventory_counts,
         to_regclass('pos.inventory_count_lines') IS NOT NULL AS has_inventory_count_lines,
         to_regclass('pos.inventory_low_stock_thresholds') IS NOT NULL AS has_inventory_low_stock_thresholds,
         to_regclass('pos.audit_events') IS NOT NULL AS has_audit_events,
         to_regclass('pos.sync_inbox_events') IS NOT NULL AS has_sync_inbox_events,
         to_regclass('pos.sync_conflicts') IS NOT NULL AS has_sync_conflicts
), table_status AS (
  SELECT *, (has_tenants AND has_stores AND has_users AND has_products AND has_units AND has_recipes AND has_recipe_items AND has_modifiers AND has_inventory_ledger AND has_inventory_counts AND has_inventory_count_lines AND has_inventory_low_stock_thresholds AND has_audit_events) AS required_tables_present
  FROM tables
), actor AS (
  SELECT u.id AS user_id
  FROM pos.users u
  JOIN params p ON p.tenant_id = u.tenant_id
  WHERE lower(u.email::text) = lower(p.admin_email)
    AND lower(coalesce(u.status,'')) = 'active'
    AND u.deleted_at IS NULL
  ORDER BY u.created_at
  LIMIT 1
), inventory_stock_before AS (
  SELECT l.tenant_id,
         l.store_id,
         l.product_id,
         l.variant_id,
         l.unit_id,
         sum(l.quantity_delta) AS quantity_on_hand
  FROM pos.inventory_ledger l
  JOIN params p ON p.tenant_id = l.tenant_id
  GROUP BY l.tenant_id, l.store_id, l.product_id, l.variant_id, l.unit_id
), negative_before AS (
  SELECT *
  FROM inventory_stock_before
  WHERE quantity_on_hand < 0
), stores_to_reconcile AS (
  SELECT DISTINCT store_id
  FROM negative_before
), inserted_counts AS (
  INSERT INTO pos.inventory_counts (
    tenant_id, store_id, terminal_id, local_count_id, status, reason, created_by_user_id, occurred_at
  )
  SELECT p.tenant_id,
         s.store_id,
         NULL,
         gen_random_uuid(),
         'completed',
         'EXP-06 inventory reconciliation hardening: zero-count correction for negative stock',
         a.user_id,
         p.occurred_at
  FROM stores_to_reconcile s
  CROSS JOIN params p
  CROSS JOIN actor a
  WHERE p.apply_reconciliation = true
  RETURNING id, tenant_id, store_id, local_count_id, occurred_at
), inserted_count_lines AS (
  INSERT INTO pos.inventory_count_lines (
    tenant_id, count_id, product_id, variant_id, unit_id, previous_quantity, counted_quantity, adjustment_delta
  )
  SELECT n.tenant_id,
         c.id,
         n.product_id,
         n.variant_id,
         n.unit_id,
         n.quantity_on_hand,
         0,
         -n.quantity_on_hand
  FROM negative_before n
  JOIN inserted_counts c ON c.tenant_id = n.tenant_id AND c.store_id = n.store_id
  RETURNING tenant_id, count_id, product_id, variant_id, unit_id, previous_quantity, counted_quantity, adjustment_delta
), inserted_ledger AS (
  INSERT INTO pos.inventory_ledger (
    tenant_id, store_id, terminal_id, product_id, variant_id, movement_type, quantity_delta, unit_id,
    cost_cents, reference_type, reference_id, source_event_id, local_occurred_at, metadata
  )
  SELECT c.tenant_id,
         c.store_id,
         NULL,
         l.product_id,
         l.variant_id,
         'adjustment',
         l.adjustment_delta,
         l.unit_id,
         NULL,
         'inventory_count',
         c.id,
         c.local_count_id,
         c.occurred_at,
         jsonb_build_object(
           'phase','EXP-06',
           'contract','inventory_reconciliation_hardening',
           'reason','negative_inventory_reconciliation',
           'previousQuantity', l.previous_quantity,
           'countedQuantity', l.counted_quantity,
           'adjustmentDelta', l.adjustment_delta
         )
  FROM inserted_count_lines l
  JOIN inserted_counts c ON c.tenant_id = l.tenant_id AND c.id = l.count_id
  RETURNING tenant_id, store_id, product_id, variant_id, unit_id, quantity_delta, reference_id, source_event_id
), adjustment_by_stock_key AS (
  SELECT tenant_id, store_id, product_id, variant_id, unit_id, sum(quantity_delta) AS adjustment_delta
  FROM inserted_ledger
  GROUP BY tenant_id, store_id, product_id, variant_id, unit_id
), inventory_stock_after_projected AS (
  SELECT b.tenant_id,
         b.store_id,
         b.product_id,
         b.variant_id,
         b.unit_id,
         b.quantity_on_hand + coalesce(a.adjustment_delta,0) AS quantity_on_hand
  FROM inventory_stock_before b
  LEFT JOIN adjustment_by_stock_key a
    ON a.tenant_id = b.tenant_id
   AND a.store_id = b.store_id
   AND a.product_id = b.product_id
   AND a.variant_id IS NOT DISTINCT FROM b.variant_id
   AND a.unit_id = b.unit_id
), modifier_semantics AS (
  SELECT count(*) FILTER (
           WHERE m.deleted_at IS NULL
             AND m.inventory_behavior NOT IN ('none','add','substitute')
         ) AS invalid_behavior_count,
         count(*) FILTER (
           WHERE m.deleted_at IS NULL
             AND m.inventory_behavior IN ('add','substitute')
             AND (m.linked_product_id IS NULL OR m.consumption_quantity IS NULL OR m.consumption_quantity <= 0 OR m.consumption_unit_id IS NULL)
         ) AS invalid_inventory_effect_count,
         count(*) FILTER (
           WHERE m.deleted_at IS NULL
             AND m.inventory_behavior = 'substitute'
             AND m.replaces_product_id IS NULL
         ) AS invalid_substitute_count,
         count(*) FILTER (
           WHERE m.deleted_at IS NULL
             AND m.inventory_behavior = 'substitute'
         ) AS substitute_modifier_count
  FROM pos.modifiers m
  JOIN params p ON p.tenant_id = m.tenant_id
), recipe_semantics AS (
  SELECT count(DISTINCT r.id) FILTER (WHERE r.status = 'active' AND r.deleted_at IS NULL) AS active_recipe_count,
         count(ri.id) FILTER (WHERE r.status = 'active' AND r.deleted_at IS NULL) AS active_recipe_item_count,
         count(ri.id) FILTER (
           WHERE r.status = 'active'
             AND r.deleted_at IS NULL
             AND (ri.quantity <= 0 OR ri.unit_id IS NULL OR ri.ingredient_product_id IS NULL)
         ) AS invalid_recipe_item_count
  FROM pos.recipes r
  JOIN params p ON p.tenant_id = r.tenant_id
  LEFT JOIN pos.recipe_items ri ON ri.tenant_id = r.tenant_id AND ri.recipe_id = r.id
), counts AS (
  SELECT EXISTS (SELECT 1 FROM pos.tenants t JOIN params p ON p.tenant_id = t.id WHERE t.status = 'active' AND t.deleted_at IS NULL) AS tenant_active,
         EXISTS (SELECT 1 FROM actor) AS admin_user_found,
         (SELECT count(*) FROM pos.stores s JOIN params p ON p.tenant_id = s.tenant_id WHERE s.status = 'active' AND s.deleted_at IS NULL) AS active_store_count,
         (SELECT count(*) FROM pos.products pr JOIN params p ON p.tenant_id = pr.tenant_id WHERE pr.is_stock_tracked = true AND pr.inventory_unit_id IS NOT NULL AND pr.status = 'active' AND pr.deleted_at IS NULL) AS stock_tracked_product_count,
         (SELECT count(*) FROM pos.inventory_low_stock_thresholds th JOIN params p ON p.tenant_id = th.tenant_id) AS low_stock_threshold_count,
         (SELECT count(*) FROM negative_before) AS negative_inventory_before_count,
         (SELECT coalesce(sum(abs(quantity_on_hand)),0) FROM negative_before) AS negative_quantity_before_total,
         (SELECT count(*) FROM inserted_counts) AS reconciliation_count_count,
         (SELECT count(*) FROM inserted_count_lines) AS reconciliation_line_count,
         (SELECT count(*) FROM inserted_ledger) AS reconciliation_ledger_count,
         (SELECT coalesce(sum(quantity_delta),0) FROM inserted_ledger) AS adjustment_quantity_total,
         (SELECT count(*) FROM inventory_stock_after_projected WHERE quantity_on_hand < 0) AS negative_inventory_after_projected_count,
         (SELECT count(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id = sie.tenant_id AND lower(coalesce(sie.status,'')) = 'retry_pending') AS retry_pending_sync_count,
         (SELECT count(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id = sie.tenant_id AND lower(coalesce(sie.status,'')) = 'dead_letter') AS dead_letter_sync_count,
         (SELECT count(*) FROM pos.sync_conflicts sc JOIN params p ON p.tenant_id = sc.tenant_id AND lower(coalesce(sc.status,'')) = 'pending') AS pending_conflict_count,
         (SELECT count(*) FROM pos.audit_events ae JOIN params p ON p.tenant_id = ae.tenant_id WHERE ae.occurred_at >= now() - interval '24 hours') AS audit_events_last_24_hours,
         4 AS schema_version
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT ts.required_tables_present THEN 'required_table_missing' END,
    CASE WHEN NOT c.tenant_active THEN 'tenant_missing_or_inactive' END,
    CASE WHEN NOT c.admin_user_found THEN 'active_admin_user_missing' END,
    CASE WHEN c.active_store_count < 2 THEN 'multi_store_inventory_context_missing' END,
    CASE WHEN c.stock_tracked_product_count < 1 THEN 'stock_tracked_products_missing' END,
    CASE WHEN c.negative_inventory_before_count > 0 AND c.reconciliation_ledger_count < c.negative_inventory_before_count THEN 'negative_inventory_reconciliation_not_applied' END,
    CASE WHEN c.negative_inventory_after_projected_count > 0 THEN 'negative_inventory_remaining_after_reconciliation' END,
    CASE WHEN ms.invalid_behavior_count > 0 THEN 'invalid_modifier_inventory_behavior' END,
    CASE WHEN ms.invalid_inventory_effect_count > 0 THEN 'invalid_modifier_inventory_effect_shape' END,
    CASE WHEN ms.invalid_substitute_count > 0 THEN 'invalid_substitute_modifier_shape' END,
    CASE WHEN rs.invalid_recipe_item_count > 0 THEN 'invalid_recipe_item_shape' END,
    CASE WHEN c.pending_conflict_count > 0 THEN 'pending_conflicts_block_inventory_reconciliation' END
  ], NULL) AS sql_blocking_reasons,
  array_remove(ARRAY[
    CASE WHEN c.retry_pending_sync_count > 0 THEN 'retry_pending_sync_requires_monitoring' END,
    CASE WHEN c.dead_letter_sync_count > 0 THEN 'dead_letter_sync_requires_triage' END,
    CASE WHEN c.low_stock_threshold_count < c.stock_tracked_product_count THEN 'low_stock_thresholds_require_review' END,
    CASE WHEN rs.active_recipe_count < 1 THEN 'active_recipe_coverage_requires_review' END,
    CASE WHEN ms.substitute_modifier_count > 0 THEN 'substitute_modifiers_verified' END
  ], NULL) AS sql_warnings
  FROM table_status ts CROSS JOIN counts c CROSS JOIN modifier_semantics ms CROSS JOIN recipe_semantics rs
)
SELECT json_build_object(
  'exp06SqlValidation', CASE WHEN array_length(b.sql_blocking_reasons,1) IS NULL THEN 'GO' ELSE 'NO-GO' END,
  'sqlBlockingReasons', coalesce(b.sql_blocking_reasons, ARRAY[]::text[]),
  'sqlWarnings', coalesce(b.sql_warnings, ARRAY[]::text[]),
  'requiredTablesPresent', ts.required_tables_present,
  'tenantActive', c.tenant_active,
  'adminUserFound', c.admin_user_found,
  'activeStoreCount', c.active_store_count,
  'stockTrackedProductCount', c.stock_tracked_product_count,
  'lowStockThresholdCount', c.low_stock_threshold_count,
  'negativeInventoryBeforeCount', c.negative_inventory_before_count,
  'negativeQuantityBeforeTotal', c.negative_quantity_before_total,
  'reconciliationCountCount', c.reconciliation_count_count,
  'reconciliationLineCount', c.reconciliation_line_count,
  'reconciliationLedgerCount', c.reconciliation_ledger_count,
  'adjustmentQuantityTotal', c.adjustment_quantity_total,
  'negativeInventoryAfterProjectedCount', c.negative_inventory_after_projected_count,
  'invalidModifierBehaviorCount', ms.invalid_behavior_count,
  'invalidModifierEffectCount', ms.invalid_inventory_effect_count,
  'invalidSubstituteModifierCount', ms.invalid_substitute_count,
  'substituteModifierCount', ms.substitute_modifier_count,
  'activeRecipeCount', rs.active_recipe_count,
  'activeRecipeItemCount', rs.active_recipe_item_count,
  'invalidRecipeItemCount', rs.invalid_recipe_item_count,
  'retryPendingSyncCount', c.retry_pending_sync_count,
  'deadLetterSyncCount', c.dead_letter_sync_count,
  'pendingConflictCount', c.pending_conflict_count,
  'auditEventsLast24Hours', c.audit_events_last_24_hours,
  'schemaVersion', c.schema_version,
  'inventoryContract', 'inventory_reconciliation_hardening'
)::text
FROM table_status ts CROSS JOIN counts c CROSS JOIN modifier_semantics ms CROSS JOIN recipe_semantics rs CROSS JOIN blockers b;
