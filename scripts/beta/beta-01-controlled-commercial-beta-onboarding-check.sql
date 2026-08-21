\set ON_ERROR_STOP on

WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         lower(:'admin_email'::text) AS admin_email,
         now() AS checked_at
), table_status AS (
  SELECT bool_and(to_regclass(table_name) IS NOT NULL) AS required_tables_present,
         coalesce(jsonb_agg(table_name) FILTER (WHERE to_regclass(table_name) IS NULL), '[]'::jsonb) AS missing_tables
  FROM (VALUES
    ('pos.tenants'),('pos.stores'),('pos.terminals'),('pos.users'),('pos.roles'),('pos.permissions'),
    ('pos.user_roles'),('pos.user_store_access'),('pos.customers'),('pos.categories'),('pos.products'),
    ('pos.product_variants'),('pos.product_barcodes'),('pos.price_lists'),('pos.product_prices'),
    ('pos.modifiers'),('pos.update_releases'),('pos.audit_events'),('pos.sync_inbox_events'),('pos.sync_conflicts')
  ) required(table_name)
), onboarding_state AS (
  SELECT
    EXISTS (SELECT 1 FROM pos.tenants t JOIN params p ON p.tenant_id=t.id WHERE t.status='active' AND t.deleted_at IS NULL) AS tenant_active,
    (SELECT COUNT(*) FROM pos.stores s JOIN params p ON p.tenant_id=s.tenant_id WHERE s.status='active' AND s.deleted_at IS NULL)::int AS active_store_count,
    (SELECT COUNT(*) FROM pos.terminals te JOIN params p ON p.tenant_id=te.tenant_id WHERE te.status='active' AND te.deleted_at IS NULL)::int AS active_terminal_count,
    (SELECT COUNT(*) FROM pos.terminals te JOIN pos.stores s ON s.tenant_id=te.tenant_id AND s.id=te.store_id JOIN params p ON p.tenant_id=te.tenant_id WHERE te.status='active' AND te.deleted_at IS NULL AND s.status='active' AND s.deleted_at IS NULL)::int AS active_terminal_with_active_store_count,
    (SELECT COUNT(*) FROM pos.users u JOIN params p ON p.tenant_id=u.tenant_id WHERE lower(u.email)=p.admin_email AND u.status='active' AND u.deleted_at IS NULL AND (u.locked_until IS NULL OR u.locked_until < p.checked_at))::int AS active_admin_count,
    (SELECT COUNT(*) FROM pos.user_roles ur JOIN pos.users u ON u.tenant_id=ur.tenant_id AND u.id=ur.user_id JOIN params p ON p.tenant_id=ur.tenant_id WHERE lower(u.email)=p.admin_email AND u.status='active' AND u.deleted_at IS NULL)::int AS admin_role_assignment_count,
    (SELECT COUNT(*) FROM pos.user_store_access usa JOIN pos.users u ON u.tenant_id=usa.tenant_id AND u.id=usa.user_id JOIN pos.stores s ON s.tenant_id=usa.tenant_id AND s.id=usa.store_id JOIN params p ON p.tenant_id=usa.tenant_id WHERE lower(u.email)=p.admin_email AND u.status='active' AND u.deleted_at IS NULL AND s.status='active' AND s.deleted_at IS NULL)::int AS admin_store_access_count,
    (SELECT COUNT(*) FROM pos.roles r JOIN params p ON p.tenant_id=r.tenant_id)::int AS role_count,
    (SELECT COUNT(*) FROM pos.permissions)::int AS permission_count,
    (SELECT COUNT(*) FROM pos.customers c JOIN params p ON p.tenant_id=c.tenant_id WHERE c.status='active' AND c.deleted_at IS NULL)::int AS active_customer_count
), catalog_state AS (
  SELECT
    (SELECT COUNT(*) FROM pos.categories c JOIN params p ON p.tenant_id=c.tenant_id WHERE c.status='active' AND c.deleted_at IS NULL)::int AS active_category_count,
    (SELECT COUNT(*) FROM pos.products pr JOIN params p ON p.tenant_id=pr.tenant_id WHERE pr.status='active' AND pr.is_sellable=true AND pr.deleted_at IS NULL)::int AS active_sellable_product_count,
    (SELECT COUNT(*) FROM pos.price_lists pl JOIN params p ON p.tenant_id=pl.tenant_id WHERE pl.status='active' AND pl.deleted_at IS NULL)::int AS active_price_list_count,
    (SELECT COUNT(*) FROM pos.product_prices pp JOIN params p ON p.tenant_id=pp.tenant_id WHERE pp.deleted_at IS NULL AND pp.currency='MXN' AND pp.price_cents >= 0 AND (pp.starts_at IS NULL OR pp.starts_at <= now()) AND (pp.ends_at IS NULL OR pp.ends_at > now()))::int AS active_mxn_price_count,
    (SELECT COUNT(*) FROM pos.product_prices pp JOIN params p ON p.tenant_id=pp.tenant_id WHERE pp.deleted_at IS NULL AND pp.price_cents < 0)::int AS negative_price_count,
    (SELECT COUNT(*) FROM pos.product_prices pp JOIN params p ON p.tenant_id=pp.tenant_id WHERE pp.deleted_at IS NULL AND pp.starts_at IS NOT NULL AND pp.ends_at IS NOT NULL AND pp.ends_at <= pp.starts_at)::int AS invalid_price_window_count,
    (SELECT COUNT(*) FROM pos.products pr JOIN params p ON p.tenant_id=pr.tenant_id WHERE pr.deleted_at IS NULL AND pr.tax_mode NOT IN ('taxable','exempt'))::int AS invalid_tax_mode_count,
    (SELECT COUNT(*) FROM pos.modifiers m JOIN params p ON p.tenant_id=m.tenant_id WHERE m.deleted_at IS NULL AND m.inventory_behavior NOT IN ('none','add','substitute'))::int AS invalid_modifier_behavior_count,
    (SELECT COUNT(*) FROM pos.modifiers m JOIN params p ON p.tenant_id=m.tenant_id WHERE m.deleted_at IS NULL AND m.inventory_behavior='substitute' AND m.replaces_product_id IS NULL)::int AS invalid_substitute_modifier_count
), release_state AS (
  SELECT
    (SELECT COUNT(*) FROM pos.update_releases ur JOIN params p ON p.tenant_id=ur.tenant_id WHERE ur.revoked_at IS NULL)::int AS active_tenant_release_count,
    (SELECT COUNT(*) FROM pos.update_releases ur JOIN params p ON p.tenant_id=ur.tenant_id WHERE ur.revoked_at IS NULL AND lower(ur.package_type)='velopack' AND coalesce(ur.universal_installer,false)=true)::int AS velopack_universal_release_count
), support_state AS (
  SELECT
    (SELECT COUNT(*) FROM pos.audit_events ae JOIN params p ON p.tenant_id=ae.tenant_id)::int AS audit_event_count,
    (SELECT COUNT(*) FROM pos.audit_events ae JOIN params p ON p.tenant_id=ae.tenant_id WHERE ae.occurred_at >= p.checked_at - interval '24 hours')::int AS audit_events_last_24_hours,
    (SELECT COUNT(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id=sie.tenant_id WHERE lower(coalesce(sie.status,''))='retry_pending')::int AS retry_pending_sync,
    (SELECT COUNT(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id=sie.tenant_id WHERE lower(coalesce(sie.status,''))='dead_letter')::int AS dead_letter_sync,
    (SELECT COUNT(*) FROM pos.sync_conflicts sc JOIN params p ON p.tenant_id=sc.tenant_id WHERE lower(coalesce(sc.status,''))='pending')::int AS pending_conflict_count,
    (SELECT COUNT(*) FROM pos.sync_inbox_events sie JOIN params p ON p.tenant_id=sie.tenant_id WHERE coalesce(sie.schema_version,0) < 4)::int AS legacy_schema_event_count
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT ts.required_tables_present THEN 'required_beta_onboarding_tables_missing' END,
    CASE WHEN NOT o.tenant_active THEN 'tenant_missing_or_inactive' END,
    CASE WHEN o.active_store_count < 1 THEN 'no_active_store' END,
    CASE WHEN o.active_terminal_count < 1 THEN 'no_active_terminal' END,
    CASE WHEN o.active_terminal_with_active_store_count < 1 THEN 'no_terminal_assigned_to_active_store' END,
    CASE WHEN o.active_admin_count <> 1 THEN 'admin_bootstrap_invalid' END,
    CASE WHEN o.admin_role_assignment_count < 1 THEN 'admin_has_no_role_assignment' END,
    CASE WHEN o.admin_store_access_count < 1 THEN 'admin_has_no_store_access' END,
    CASE WHEN o.role_count < 1 THEN 'no_roles_configured' END,
    CASE WHEN o.permission_count < 1 THEN 'no_permissions_configured' END,
    CASE WHEN o.active_customer_count < 1 THEN 'no_active_beta_customer_profile' END,
    CASE WHEN c.active_category_count < 1 THEN 'no_active_category' END,
    CASE WHEN c.active_sellable_product_count < 1 THEN 'no_active_sellable_product' END,
    CASE WHEN c.active_price_list_count < 1 THEN 'no_active_price_list' END,
    CASE WHEN c.active_mxn_price_count < 1 THEN 'no_active_mxn_price' END,
    CASE WHEN c.negative_price_count > 0 THEN 'negative_price_detected' END,
    CASE WHEN c.invalid_price_window_count > 0 THEN 'invalid_price_window_detected' END,
    CASE WHEN c.invalid_tax_mode_count > 0 THEN 'invalid_tax_mode_detected' END,
    CASE WHEN c.invalid_modifier_behavior_count > 0 THEN 'invalid_modifier_behavior_detected' END,
    CASE WHEN c.invalid_substitute_modifier_count > 0 THEN 'invalid_substitute_modifier_detected' END,
    CASE WHEN r.active_tenant_release_count < 1 THEN 'no_active_tenant_release' END,
    CASE WHEN r.velopack_universal_release_count < 1 THEN 'no_velopack_universal_release' END,
    CASE WHEN s.audit_event_count < 1 THEN 'no_audit_evidence' END,
    CASE WHEN s.pending_conflict_count > 0 THEN 'pending_sync_conflicts' END,
    CASE WHEN s.legacy_schema_event_count > 0 THEN 'legacy_sync_schema_events' END
  ], NULL) AS items
  FROM table_status ts CROSS JOIN onboarding_state o CROSS JOIN catalog_state c CROSS JOIN release_state r CROSS JOIN support_state s
), conditions AS (
  SELECT array_remove(ARRAY[
    CASE WHEN s.retry_pending_sync > 0 THEN 'retry_pending_sync_requires_monitoring' END,
    CASE WHEN s.dead_letter_sync > 0 THEN 'dead_letter_sync_requires_triage' END,
    CASE WHEN s.audit_events_last_24_hours < 1 THEN 'audit_events_last_24_hours_low' END
  ], NULL) AS items
  FROM support_state s
)
SELECT jsonb_build_object(
  'beta01SqlValidation', CASE WHEN cardinality((SELECT items FROM blockers))=0 THEN 'GO' ELSE 'NO-GO' END,
  'requiredTablesPresent', (SELECT required_tables_present FROM table_status),
  'missingTables', (SELECT missing_tables FROM table_status),
  'tenantActive', (SELECT tenant_active FROM onboarding_state),
  'activeStoreCount', (SELECT active_store_count FROM onboarding_state),
  'activeTerminalCount', (SELECT active_terminal_count FROM onboarding_state),
  'activeTerminalWithActiveStoreCount', (SELECT active_terminal_with_active_store_count FROM onboarding_state),
  'activeAdminCount', (SELECT active_admin_count FROM onboarding_state),
  'adminRoleAssignmentCount', (SELECT admin_role_assignment_count FROM onboarding_state),
  'adminStoreAccessCount', (SELECT admin_store_access_count FROM onboarding_state),
  'roleCount', (SELECT role_count FROM onboarding_state),
  'permissionCount', (SELECT permission_count FROM onboarding_state),
  'activeCustomerCount', (SELECT active_customer_count FROM onboarding_state),
  'activeCategoryCount', (SELECT active_category_count FROM catalog_state),
  'activeSellableProductCount', (SELECT active_sellable_product_count FROM catalog_state),
  'activePriceListCount', (SELECT active_price_list_count FROM catalog_state),
  'activeMxnPriceCount', (SELECT active_mxn_price_count FROM catalog_state),
  'negativePriceCount', (SELECT negative_price_count FROM catalog_state),
  'invalidPriceWindowCount', (SELECT invalid_price_window_count FROM catalog_state),
  'invalidTaxModeCount', (SELECT invalid_tax_mode_count FROM catalog_state),
  'invalidModifierBehaviorCount', (SELECT invalid_modifier_behavior_count FROM catalog_state),
  'invalidSubstituteModifierCount', (SELECT invalid_substitute_modifier_count FROM catalog_state),
  'activeTenantReleaseCount', (SELECT active_tenant_release_count FROM release_state),
  'velopackUniversalReleaseCount', (SELECT velopack_universal_release_count FROM release_state),
  'auditEventCount', (SELECT audit_event_count FROM support_state),
  'auditEventsLast24Hours', (SELECT audit_events_last_24_hours FROM support_state),
  'retryPendingSync', (SELECT retry_pending_sync FROM support_state),
  'deadLetterSync', (SELECT dead_letter_sync FROM support_state),
  'pendingConflictCount', (SELECT pending_conflict_count FROM support_state),
  'legacySchemaEventCount', (SELECT legacy_schema_event_count FROM support_state),
  'blockers', (SELECT to_jsonb(items) FROM blockers),
  'conditions', (SELECT to_jsonb(items) FROM conditions),
  'schemaVersion', 4,
  'syncContract', 'schema_version_4',
  'betaContract', 'controlled_commercial_beta_onboarding'
)::text;
