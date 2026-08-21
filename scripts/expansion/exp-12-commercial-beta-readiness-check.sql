
\set ON_ERROR_STOP on

WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id, now() AS checked_at
), table_status AS (
  SELECT bool_and(to_regclass(table_name) IS NOT NULL) AS required_tables_present,
         coalesce(jsonb_agg(table_name) FILTER (WHERE to_regclass(table_name) IS NULL), '[]'::jsonb) AS missing_tables
  FROM (VALUES
    ('pos.tenants'),('pos.stores'),('pos.terminals'),('pos.users'),('pos.roles'),('pos.permissions'),
    ('pos.user_roles'),('pos.user_store_access'),('pos.customers'),('pos.categories'),('pos.products'),
    ('pos.product_variants'),('pos.product_barcodes'),('pos.price_lists'),('pos.product_prices'),
    ('pos.modifier_groups'),('pos.modifiers'),('pos.sales'),('pos.payments'),('pos.cash_shifts'),
    ('pos.sync_inbox_events'),('pos.sync_conflicts'),('pos.sync_changes'),('pos.update_releases'),('pos.audit_events')
  ) required(table_name)
), tenant_state AS (
  SELECT
    EXISTS (SELECT 1 FROM pos.tenants t JOIN params p ON p.tenant_id=t.id WHERE t.status='active' AND t.deleted_at IS NULL) AS tenant_active,
    (SELECT COUNT(*) FROM pos.stores s JOIN params p ON p.tenant_id=s.tenant_id WHERE s.status='active' AND s.deleted_at IS NULL)::int AS active_store_count,
    (SELECT COUNT(*) FROM pos.terminals te JOIN params p ON p.tenant_id=te.tenant_id WHERE te.status='active')::int AS active_terminal_count,
    (SELECT COUNT(*) FROM pos.users u JOIN params p ON p.tenant_id=u.tenant_id WHERE u.status='active' AND u.deleted_at IS NULL)::int AS active_user_count,
    (SELECT COUNT(*) FROM pos.roles r JOIN params p ON p.tenant_id=r.tenant_id)::int AS role_count,
    (SELECT COUNT(*) FROM pos.permissions)::int AS permission_count,
    (SELECT COUNT(*) FROM pos.user_roles ur JOIN params p ON p.tenant_id=ur.tenant_id)::int AS role_assignment_count,
    (SELECT COUNT(*) FROM pos.user_store_access usa JOIN params p ON p.tenant_id=usa.tenant_id)::int AS store_access_assignment_count,
    (SELECT COUNT(*) FROM pos.customers c JOIN params p ON p.tenant_id=c.tenant_id WHERE c.status='active' AND c.deleted_at IS NULL)::int AS active_customer_count
), catalog_state AS (
  SELECT
    (SELECT COUNT(*) FROM pos.categories c JOIN params p ON p.tenant_id=c.tenant_id WHERE c.status='active' AND c.deleted_at IS NULL)::int AS active_category_count,
    (SELECT COUNT(*) FROM pos.products pr JOIN params p ON p.tenant_id=pr.tenant_id WHERE pr.status='active' AND pr.is_sellable=true AND pr.deleted_at IS NULL)::int AS active_sellable_product_count,
    (SELECT COUNT(*) FROM pos.product_variants v JOIN params p ON p.tenant_id=v.tenant_id WHERE v.status='active' AND v.deleted_at IS NULL)::int AS active_variant_count,
    (SELECT COUNT(*) FROM pos.product_barcodes b JOIN params p ON p.tenant_id=b.tenant_id WHERE b.deleted_at IS NULL)::int AS barcode_count,
    (SELECT COUNT(*) FROM pos.price_lists pl JOIN params p ON p.tenant_id=pl.tenant_id WHERE pl.status='active' AND pl.deleted_at IS NULL)::int AS active_price_list_count,
    (SELECT COUNT(*) FROM pos.product_prices pp JOIN params p ON p.tenant_id=pp.tenant_id WHERE pp.deleted_at IS NULL AND pp.currency='MXN' AND pp.price_cents >= 0 AND (pp.starts_at IS NULL OR pp.starts_at <= now()) AND (pp.ends_at IS NULL OR pp.ends_at > now()))::int AS active_mxn_price_count,
    (SELECT COUNT(*) FROM pos.product_prices pp JOIN params p ON p.tenant_id=pp.tenant_id WHERE pp.deleted_at IS NULL AND pp.price_cents < 0)::int AS negative_price_count,
    (SELECT COUNT(*) FROM pos.product_prices pp JOIN params p ON p.tenant_id=pp.tenant_id WHERE pp.deleted_at IS NULL AND pp.starts_at IS NOT NULL AND pp.ends_at IS NOT NULL AND pp.ends_at <= pp.starts_at)::int AS invalid_price_window_count,
    (SELECT COUNT(*) FROM pos.products pr JOIN params p ON p.tenant_id=pr.tenant_id WHERE pr.deleted_at IS NULL AND pr.tax_mode NOT IN ('taxable','exempt'))::int AS invalid_tax_mode_count,
    (SELECT COUNT(*) FROM pos.modifiers m JOIN params p ON p.tenant_id=m.tenant_id WHERE m.deleted_at IS NULL AND m.inventory_behavior NOT IN ('none','add','substitute'))::int AS invalid_modifier_behavior_count,
    (SELECT COUNT(*) FROM pos.modifiers m JOIN params p ON p.tenant_id=m.tenant_id WHERE m.deleted_at IS NULL AND m.inventory_behavior='substitute' AND m.replaces_product_id IS NULL)::int AS invalid_substitute_modifier_count
), ops_state AS (
  SELECT
    (SELECT COUNT(*) FROM pos.sales s JOIN params p ON p.tenant_id=s.tenant_id)::int AS total_sales_count,
    (SELECT COUNT(*) FROM pos.sales s JOIN params p ON p.tenant_id=s.tenant_id WHERE lower(coalesce(s.status,''))='completed')::int AS completed_sales_count,
    (SELECT COUNT(*) FROM pos.payments py JOIN params p ON p.tenant_id=py.tenant_id WHERE lower(coalesce(py.status,''))='approved')::int AS approved_payment_count,
    (SELECT COUNT(*) FROM pos.payments py JOIN params p ON p.tenant_id=py.tenant_id WHERE lower(coalesce(py.status,''))='failed' AND py.created_at >= p.checked_at - interval '24 hours')::int AS failed_payments_last_24_hours,
    (SELECT COUNT(*) FROM pos.cash_shifts cs JOIN params p ON p.tenant_id=cs.tenant_id WHERE lower(coalesce(cs.status,''))='open')::int AS open_shift_count,
    (SELECT COUNT(*) FROM pos.cash_shifts cs JOIN params p ON p.tenant_id=cs.tenant_id WHERE cs.closed_at >= p.checked_at - interval '24 hours' AND coalesce(cs.difference_cents,0) <> 0)::int AS cash_difference_last_24_hours_count
), sync_state AS (
  SELECT
    (SELECT COUNT(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id=sie.tenant_id)::int AS total_sync_events,
    (SELECT COUNT(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id=sie.tenant_id WHERE lower(coalesce(sie.status,''))='processed')::int AS processed_sync_count,
    (SELECT COUNT(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id=sie.tenant_id WHERE lower(coalesce(sie.status,''))='retry_pending')::int AS retry_pending_sync,
    (SELECT COUNT(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id=sie.tenant_id WHERE lower(coalesce(sie.status,''))='dead_letter')::int AS dead_letter_sync,
    (SELECT COUNT(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id=sie.tenant_id WHERE lower(coalesce(sie.status,''))='retry_pending' AND sie.next_retry_at IS NOT NULL AND sie.next_retry_at <= p.checked_at)::int AS retry_due_count,
    (SELECT COUNT(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id=sie.tenant_id WHERE coalesce(sie.schema_version,0) < 4)::int AS legacy_schema_event_count,
    (SELECT COUNT(*) FROM pos.sync_conflicts sc JOIN params p ON p.tenant_id=sc.tenant_id WHERE lower(coalesce(sc.status,''))='pending')::int AS pending_conflict_count,
    (SELECT COUNT(*) FROM pos.sync_conflicts sc JOIN params p ON p.tenant_id=sc.tenant_id WHERE lower(coalesce(sc.status,''))='resolved')::int AS resolved_conflict_count,
    (SELECT COUNT(*) FROM pos.sync_changes c JOIN params p ON p.tenant_id=c.tenant_id)::int AS sync_change_count
), release_state AS (
  SELECT
    (SELECT COUNT(*) FROM pos.update_releases ur JOIN params p ON p.tenant_id=ur.tenant_id)::int AS tenant_release_count,
    (SELECT COUNT(*) FROM pos.update_releases ur JOIN params p ON p.tenant_id=ur.tenant_id WHERE lower(coalesce(ur.channel,''))='internal')::int AS internal_release_count,
    (SELECT COUNT(*) FROM pos.update_releases ur JOIN params p ON p.tenant_id=ur.tenant_id WHERE lower(coalesce(ur.channel,''))='stable')::int AS stable_release_count,
    (SELECT COUNT(*) FROM pos.update_releases ur JOIN params p ON p.tenant_id=ur.tenant_id WHERE lower(coalesce(ur.package_type,''))='velopack' AND coalesce(ur.universal_installer,false)=true)::int AS velopack_universal_release_count
), audit_state AS (
  SELECT
    (SELECT COUNT(*) FROM pos.audit_events ae JOIN params p ON p.tenant_id=ae.tenant_id)::int AS audit_event_count,
    (SELECT COUNT(*) FROM pos.audit_events ae JOIN params p ON p.tenant_id=ae.tenant_id WHERE ae.occurred_at >= p.checked_at - interval '24 hours')::int AS audit_events_last_24_hours
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT ts.required_tables_present THEN 'required_commercial_beta_tables_missing' END,
    CASE WHEN NOT t.tenant_active THEN 'tenant_missing_or_inactive' END,
    CASE WHEN t.active_store_count < 1 THEN 'no_active_store' END,
    CASE WHEN t.active_terminal_count < 1 THEN 'no_active_terminal' END,
    CASE WHEN t.active_user_count < 1 THEN 'no_active_user' END,
    CASE WHEN t.role_count < 1 THEN 'no_roles_configured' END,
    CASE WHEN t.permission_count < 1 THEN 'no_permissions_configured' END,
    CASE WHEN t.role_assignment_count < 1 THEN 'no_role_assignments' END,
    CASE WHEN c.active_category_count < 1 THEN 'no_active_category' END,
    CASE WHEN c.active_sellable_product_count < 1 THEN 'no_active_sellable_product' END,
    CASE WHEN c.active_price_list_count < 1 THEN 'no_active_price_list' END,
    CASE WHEN c.active_mxn_price_count < 1 THEN 'no_active_mxn_price' END,
    CASE WHEN c.negative_price_count > 0 THEN 'negative_price_detected' END,
    CASE WHEN c.invalid_price_window_count > 0 THEN 'invalid_price_window_detected' END,
    CASE WHEN c.invalid_tax_mode_count > 0 THEN 'invalid_tax_mode_detected' END,
    CASE WHEN c.invalid_modifier_behavior_count > 0 THEN 'invalid_modifier_behavior_detected' END,
    CASE WHEN c.invalid_substitute_modifier_count > 0 THEN 'invalid_substitute_modifier_detected' END,
    CASE WHEN o.failed_payments_last_24_hours > 0 THEN 'failed_payments_last_24_hours' END,
    CASE WHEN o.cash_difference_last_24_hours_count > 0 THEN 'cash_difference_last_24_hours' END,
    CASE WHEN s.pending_conflict_count > 0 THEN 'pending_sync_conflicts' END,
    CASE WHEN s.legacy_schema_event_count > 0 THEN 'legacy_sync_schema_events' END,
    CASE WHEN r.tenant_release_count < 1 THEN 'no_tenant_release_channel_evidence' END,
    CASE WHEN r.velopack_universal_release_count < 1 THEN 'no_velopack_universal_release_evidence' END,
    CASE WHEN a.audit_event_count < 1 THEN 'no_audit_events' END
  ], NULL) AS items
  FROM table_status ts CROSS JOIN tenant_state t CROSS JOIN catalog_state c CROSS JOIN ops_state o CROSS JOIN sync_state s CROSS JOIN release_state r CROSS JOIN audit_state a
), warnings AS (
  SELECT array_remove(ARRAY[
    CASE WHEN t.active_customer_count < 1 THEN 'no_active_customer_records' END,
    CASE WHEN t.store_access_assignment_count < 1 THEN 'no_store_access_assignments' END,
    CASE WHEN o.open_shift_count > 0 THEN 'open_cash_shift_requires_daily_review' END,
    CASE WHEN s.retry_pending_sync > 0 THEN 'retry_pending_sync_requires_monitoring' END,
    CASE WHEN s.retry_due_count > 0 THEN 'retry_due_requires_worker_or_manual_retry' END,
    CASE WHEN s.dead_letter_sync > 0 THEN 'dead_letter_requires_support_ticket' END,
    CASE WHEN r.stable_release_count < 1 THEN 'stable_channel_promotion_pending' END,
    CASE WHEN a.audit_events_last_24_hours < 1 THEN 'audit_events_last_24_hours_low' END
  ], NULL) AS items
  FROM tenant_state t CROSS JOIN ops_state o CROSS JOIN sync_state s CROSS JOIN release_state r CROSS JOIN audit_state a
)
SELECT jsonb_build_object(
  'exp12SqlValidation', CASE WHEN cardinality((SELECT items FROM blockers)) = 0 THEN 'GO' ELSE 'NO-GO' END,
  'requiredTablesPresent', (SELECT required_tables_present FROM table_status),
  'missingTables', (SELECT missing_tables FROM table_status),
  'tenantActive', (SELECT tenant_active FROM tenant_state),
  'activeStoreCount', (SELECT active_store_count FROM tenant_state),
  'activeTerminalCount', (SELECT active_terminal_count FROM tenant_state),
  'activeUserCount', (SELECT active_user_count FROM tenant_state),
  'roleCount', (SELECT role_count FROM tenant_state),
  'permissionCount', (SELECT permission_count FROM tenant_state),
  'roleAssignmentCount', (SELECT role_assignment_count FROM tenant_state),
  'storeAccessAssignmentCount', (SELECT store_access_assignment_count FROM tenant_state),
  'activeCustomerCount', (SELECT active_customer_count FROM tenant_state),
  'activeCategoryCount', (SELECT active_category_count FROM catalog_state),
  'activeSellableProductCount', (SELECT active_sellable_product_count FROM catalog_state),
  'activeVariantCount', (SELECT active_variant_count FROM catalog_state),
  'barcodeCount', (SELECT barcode_count FROM catalog_state),
  'activePriceListCount', (SELECT active_price_list_count FROM catalog_state),
  'activeMxnPriceCount', (SELECT active_mxn_price_count FROM catalog_state),
  'negativePriceCount', (SELECT negative_price_count FROM catalog_state),
  'invalidPriceWindowCount', (SELECT invalid_price_window_count FROM catalog_state),
  'invalidTaxModeCount', (SELECT invalid_tax_mode_count FROM catalog_state),
  'invalidModifierBehaviorCount', (SELECT invalid_modifier_behavior_count FROM catalog_state),
  'invalidSubstituteModifierCount', (SELECT invalid_substitute_modifier_count FROM catalog_state),
  'totalSalesCount', (SELECT total_sales_count FROM ops_state),
  'completedSalesCount', (SELECT completed_sales_count FROM ops_state),
  'approvedPaymentCount', (SELECT approved_payment_count FROM ops_state),
  'failedPaymentsLast24Hours', (SELECT failed_payments_last_24_hours FROM ops_state),
  'openShiftCount', (SELECT open_shift_count FROM ops_state),
  'cashDifferenceLast24HoursCount', (SELECT cash_difference_last_24_hours_count FROM ops_state),
  'totalSyncEvents', (SELECT total_sync_events FROM sync_state),
  'processedSyncCount', (SELECT processed_sync_count FROM sync_state),
  'retryPendingSync', (SELECT retry_pending_sync FROM sync_state),
  'deadLetterSync', (SELECT dead_letter_sync FROM sync_state),
  'retryDueCount', (SELECT retry_due_count FROM sync_state),
  'legacySchemaEventCount', (SELECT legacy_schema_event_count FROM sync_state),
  'pendingConflictCount', (SELECT pending_conflict_count FROM sync_state),
  'resolvedConflictCount', (SELECT resolved_conflict_count FROM sync_state),
  'syncChangeCount', (SELECT sync_change_count FROM sync_state),
  'tenantReleaseCount', (SELECT tenant_release_count FROM release_state),
  'internalReleaseCount', (SELECT internal_release_count FROM release_state),
  'stableReleaseCount', (SELECT stable_release_count FROM release_state),
  'velopackUniversalReleaseCount', (SELECT velopack_universal_release_count FROM release_state),
  'auditEventCount', (SELECT audit_event_count FROM audit_state),
  'auditEventsLast24Hours', (SELECT audit_events_last_24_hours FROM audit_state),
  'blockers', (SELECT to_jsonb(items) FROM blockers),
  'sqlWarnings', (SELECT to_jsonb(items) FROM warnings),
  'schemaVersion', 4,
  'commercialBetaContract', 'commercial_beta_readiness'
)::text;
