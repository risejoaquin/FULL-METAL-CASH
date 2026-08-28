\set ON_ERROR_STOP on

WITH scoped AS (
  SELECT :'tenant_id'::uuid AS tenant_id
), checks AS (
  SELECT
    (SELECT to_regclass('pos.tenants') IS NOT NULL) AS has_tenants,
    (SELECT to_regclass('pos.stores') IS NOT NULL) AS has_stores,
    (SELECT to_regclass('pos.users') IS NOT NULL) AS has_users,
    (SELECT to_regclass('pos.sales') IS NOT NULL) AS has_sales,
    (SELECT to_regclass('pos.payments') IS NOT NULL) AS has_payments,
    (SELECT to_regclass('pos.inventory_ledger') IS NOT NULL) AS has_inventory_ledger,
    (SELECT to_regclass('pos.sync_inbox_events') IS NOT NULL) AS has_sync_inbox_events,
    (SELECT to_regclass('pos.sync_conflicts') IS NOT NULL) AS has_sync_conflicts,
    (SELECT to_regclass('pos.audit_events') IS NOT NULL) AS has_audit_events,
    (SELECT count(*) = 1 FROM pos.tenants t, scoped s WHERE t.id = s.tenant_id) AS tenant_exists,
    (SELECT count(*)::bigint FROM pos.stores st, scoped s WHERE st.tenant_id = s.tenant_id) AS store_count,
    (SELECT count(*)::bigint FROM pos.users u, scoped s WHERE u.tenant_id = s.tenant_id) AS user_count,
    (SELECT count(*)::bigint FROM pos.sales sa, scoped s WHERE sa.tenant_id = s.tenant_id) AS sales_count,
    (SELECT count(*)::bigint FROM pos.payments p, scoped s WHERE p.tenant_id = s.tenant_id) AS payment_count,
    (SELECT count(*)::bigint FROM pos.audit_events a, scoped s WHERE a.tenant_id = s.tenant_id) AS audit_event_count,
    (SELECT count(*)::bigint FROM pos.sync_inbox_events e, scoped s WHERE e.tenant_id = s.tenant_id) AS sync_inbox_count,
    (SELECT count(*)::bigint FROM pos.sync_conflicts c, scoped s WHERE c.tenant_id = s.tenant_id) AS sync_conflict_count
)
SELECT json_build_object(
  'requiredTablesPresent', (has_tenants AND has_stores AND has_users AND has_sales AND has_payments AND has_inventory_ledger AND has_sync_inbox_events AND has_sync_conflicts AND has_audit_events),
  'tenantExists', tenant_exists,
  'storeCount', store_count,
  'userCount', user_count,
  'salesCount', sales_count,
  'paymentCount', payment_count,
  'auditEventCount', audit_event_count,
  'syncInboxCount', sync_inbox_count,
  'syncConflictCount', sync_conflict_count,
  'pilot08SourceValidation', CASE WHEN (has_tenants AND has_stores AND has_users AND has_sales AND has_payments AND has_inventory_ledger AND has_sync_inbox_events AND has_sync_conflicts AND has_audit_events AND tenant_exists) THEN 'GO' ELSE 'NO-GO' END
)::text
FROM checks;
