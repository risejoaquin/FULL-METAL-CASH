WITH params AS (
  SELECT
    :'tenant_id'::uuid AS tenant_id,
    :'customer_id'::uuid AS customer_id,
    :'user_id'::uuid AS user_id,
    :'store_id'::uuid AS store_id,
    :'role_code'::text AS role_code
),
required_tables AS (
  SELECT required.table_name, EXISTS (
    SELECT 1 FROM information_schema.tables t
    WHERE t.table_schema='pos' AND t.table_name=required.table_name
  ) AS present
  FROM (VALUES
    ('tenants'),('stores'),('users'),('roles'),('permissions'),('user_roles'),('user_store_access'),
    ('terminals'),('customers'),('sales'),('audit_events')
  ) AS required(table_name)
),
counts AS (
  SELECT
    (SELECT COUNT(*) FROM pos.tenants t JOIN params p ON p.tenant_id=t.id WHERE t.deleted_at IS NULL)::int AS tenant_count,
    (SELECT COUNT(*) FROM pos.stores s JOIN params p ON p.tenant_id=s.tenant_id WHERE s.deleted_at IS NULL)::int AS store_count,
    (SELECT COUNT(*) FROM pos.stores s JOIN params p ON p.tenant_id=s.tenant_id WHERE s.status='active' AND s.deleted_at IS NULL)::int AS active_store_count,
    (SELECT COUNT(*) FROM pos.terminals te JOIN params p ON p.tenant_id=te.tenant_id)::int AS terminal_count,
    (SELECT COUNT(*) FROM pos.users u JOIN params p ON p.tenant_id=u.tenant_id WHERE u.deleted_at IS NULL)::int AS user_count,
    (SELECT COUNT(*) FROM pos.roles r JOIN params p ON p.tenant_id=r.tenant_id)::int AS role_count,
    (SELECT COUNT(*) FROM pos.permissions)::int AS permission_count,
    (SELECT COUNT(*) FROM pos.customers c JOIN params p ON p.tenant_id=c.tenant_id WHERE c.deleted_at IS NULL)::int AS customer_count,
    (SELECT COUNT(*) FROM pos.users u JOIN params p ON p.tenant_id=u.tenant_id AND p.user_id=u.id WHERE u.status='active' AND u.deleted_at IS NULL)::int AS exp10_user_exists,
    (SELECT COUNT(*) FROM pos.customers c JOIN params p ON p.tenant_id=c.tenant_id AND p.customer_id=c.id WHERE c.status='active' AND c.deleted_at IS NULL)::int AS exp10_customer_exists,
    (SELECT COUNT(*) FROM pos.user_store_access usa JOIN params p ON p.tenant_id=usa.tenant_id AND p.user_id=usa.user_id AND p.store_id=usa.store_id)::int AS exp10_store_access_count,
    (SELECT COUNT(*) FROM pos.user_roles ur JOIN pos.roles r ON r.tenant_id=ur.tenant_id AND r.id=ur.role_id JOIN params p ON p.tenant_id=ur.tenant_id AND p.user_id=ur.user_id WHERE r.code=p.role_code)::int AS exp10_role_assignment_count,
    (SELECT COUNT(*) FROM pos.audit_events ae JOIN params p ON p.tenant_id=ae.tenant_id WHERE ae.entity_type='customer' AND ae.entity_id::text=p.customer_id::text AND ae.action IN ('customer.created','customer.updated'))::int AS exp10_customer_audit_count,
    (SELECT COUNT(*) FROM pos.audit_events ae JOIN params p ON p.tenant_id=ae.tenant_id WHERE ae.entity_type='user' AND ae.entity_id::text=p.user_id::text AND ae.action IN ('user.created','user.updated'))::int AS exp10_user_audit_count,
    (SELECT COUNT(*) FROM pos.audit_events ae JOIN params p ON p.tenant_id=ae.tenant_id WHERE ae.occurred_at >= now() - interval '24 hours')::int AS audit_events_last_24_hours
),
blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN EXISTS(SELECT 1 FROM required_tables WHERE present=false) THEN 'required_admin_tables_missing' END,
    CASE WHEN tenant_count <> 1 THEN 'tenant_not_found' END,
    CASE WHEN active_store_count < 1 THEN 'active_store_required' END,
    CASE WHEN role_count < 1 THEN 'role_catalog_required' END,
    CASE WHEN permission_count < 1 THEN 'permission_catalog_required' END,
    CASE WHEN exp10_user_exists <> 1 THEN 'exp10_user_missing' END,
    CASE WHEN exp10_customer_exists <> 1 THEN 'exp10_customer_missing' END,
    CASE WHEN exp10_store_access_count < 1 THEN 'exp10_user_store_access_missing' END,
    CASE WHEN exp10_role_assignment_count < 1 THEN 'exp10_user_role_assignment_missing' END,
    CASE WHEN exp10_customer_audit_count < 2 THEN 'exp10_customer_audit_missing' END,
    CASE WHEN exp10_user_audit_count < 2 THEN 'exp10_user_audit_missing' END
  ], NULL) AS items
  FROM counts
),
warnings AS (
  SELECT array_remove(ARRAY[
    CASE WHEN terminal_count < 1 THEN 'no_terminals_registered' END,
    CASE WHEN customer_count < 1 THEN 'no_customers_visible' END
  ], NULL) AS items
  FROM counts
)
SELECT jsonb_build_object(
  'exp10SqlValidation', CASE WHEN cardinality((SELECT items FROM blockers)) = 0 THEN 'GO' ELSE 'NO-GO' END,
  'requiredTablesPresent', NOT EXISTS(SELECT 1 FROM required_tables WHERE present=false),
  'missingTables', COALESCE((SELECT jsonb_agg(table_name) FROM required_tables WHERE present=false), '[]'::jsonb),
  'tenantCount', (SELECT tenant_count FROM counts),
  'storeCount', (SELECT store_count FROM counts),
  'activeStoreCount', (SELECT active_store_count FROM counts),
  'terminalCount', (SELECT terminal_count FROM counts),
  'userCount', (SELECT user_count FROM counts),
  'roleCount', (SELECT role_count FROM counts),
  'permissionCount', (SELECT permission_count FROM counts),
  'customerCount', (SELECT customer_count FROM counts),
  'exp10UserExists', (SELECT exp10_user_exists FROM counts),
  'exp10CustomerExists', (SELECT exp10_customer_exists FROM counts),
  'exp10StoreAccessCount', (SELECT exp10_store_access_count FROM counts),
  'exp10RoleAssignmentCount', (SELECT exp10_role_assignment_count FROM counts),
  'exp10CustomerAuditCount', (SELECT exp10_customer_audit_count FROM counts),
  'exp10UserAuditCount', (SELECT exp10_user_audit_count FROM counts),
  'auditEventsLast24Hours', (SELECT audit_events_last_24_hours FROM counts),
  'blockers', (SELECT to_jsonb(items) FROM blockers),
  'sqlWarnings', (SELECT to_jsonb(items) FROM warnings),
  'schemaVersion', 4,
  'adminManagementContract', 'customer_admin_management_completion'
)::text;
