\set ON_ERROR_STOP on

WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         lower(:'admin_email'::text) AS admin_email
), required_tables AS (
  SELECT bool_and(to_regclass(table_name) IS NOT NULL) AS ok,
         coalesce(jsonb_agg(table_name) FILTER (WHERE to_regclass(table_name) IS NULL), '[]'::jsonb) AS missing
  FROM (VALUES
    ('pos.tenants'),('pos.tenant_configs'),('pos.production_bootstrap_runs'),('pos.stores'),('pos.terminals'),
    ('pos.users'),('pos.roles'),('pos.permissions'),('pos.role_permissions'),('pos.user_roles'),('pos.user_store_access'),
    ('pos.categories'),('pos.products'),('pos.product_variants'),('pos.price_lists'),('pos.product_prices'),
    ('pos.customers'),('pos.sales'),('pos.update_releases'),('pos.audit_events')
  ) t(table_name)
), target AS (
  SELECT
    EXISTS (SELECT 1 FROM pos.tenants t JOIN params p ON p.tenant_id=t.id WHERE t.status='active' AND t.deleted_at IS NULL) AS tenant_active,
    (SELECT count(*) FROM pos.tenant_configs tc JOIN params p ON p.tenant_id=tc.tenant_id)::int AS tenant_config_count,
    (SELECT count(*) FROM pos.production_bootstrap_runs r JOIN params p ON p.tenant_id=r.tenant_id WHERE r.status='completed')::int AS completed_bootstrap_run_count,
    (SELECT count(*) FROM pos.stores s JOIN params p ON p.tenant_id=s.tenant_id WHERE s.status='active' AND s.deleted_at IS NULL)::int AS active_store_count,
    (SELECT count(*) FROM pos.terminals t JOIN params p ON p.tenant_id=t.tenant_id WHERE t.status='active' AND t.deleted_at IS NULL)::int AS active_terminal_count,
    (SELECT count(*) FROM pos.users u JOIN params p ON p.tenant_id=u.tenant_id WHERE lower(u.email)=p.admin_email AND u.status='active' AND u.deleted_at IS NULL)::int AS active_admin_count,
    (SELECT count(*) FROM pos.roles r JOIN params p ON p.tenant_id=r.tenant_id WHERE r.code IN ('owner','admin','manager','cashier'))::int AS seeded_role_count,
    (SELECT count(DISTINCT rp.permission_code) FROM pos.role_permissions rp JOIN params p ON p.tenant_id=rp.tenant_id)::int AS seeded_permission_assignment_count,
    (SELECT count(*) FROM pos.user_roles ur JOIN pos.users u ON u.id=ur.user_id AND u.tenant_id=ur.tenant_id JOIN pos.roles r ON r.id=ur.role_id AND r.tenant_id=ur.tenant_id JOIN params p ON p.tenant_id=ur.tenant_id WHERE lower(u.email)=p.admin_email AND r.code='owner')::int AS admin_owner_assignment_count,
    (SELECT count(*) FROM pos.user_store_access usa JOIN pos.users u ON u.id=usa.user_id AND u.tenant_id=usa.tenant_id JOIN params p ON p.tenant_id=usa.tenant_id WHERE lower(u.email)=p.admin_email)::int AS admin_store_access_count,
    (SELECT count(*) FROM pos.categories c JOIN params p ON p.tenant_id=c.tenant_id WHERE c.status='active' AND c.deleted_at IS NULL)::int AS active_category_count,
    (SELECT count(*) FROM pos.products pr JOIN params p ON p.tenant_id=pr.tenant_id WHERE pr.status='active' AND pr.is_sellable=true AND pr.deleted_at IS NULL)::int AS active_product_count,
    (SELECT count(*) FROM pos.price_lists pl JOIN params p ON p.tenant_id=pl.tenant_id WHERE pl.status='active' AND pl.deleted_at IS NULL)::int AS active_price_list_count,
    (SELECT count(*) FROM pos.update_releases ur JOIN params p ON p.tenant_id=ur.tenant_id WHERE ur.revoked_at IS NULL)::int AS tenant_release_count,
    (SELECT coalesce(jsonb_agg(id::text ORDER BY id), '[]'::jsonb) FROM pos.stores s JOIN params p ON p.tenant_id=s.tenant_id WHERE s.deleted_at IS NULL) AS store_ids,
    (SELECT coalesce(jsonb_agg(id::text ORDER BY id), '[]'::jsonb) FROM pos.users u JOIN params p ON p.tenant_id=u.tenant_id WHERE u.deleted_at IS NULL) AS user_ids,
    (SELECT coalesce(jsonb_agg(id::text ORDER BY id), '[]'::jsonb) FROM pos.terminals t JOIN params p ON p.tenant_id=t.tenant_id WHERE t.deleted_at IS NULL) AS terminal_ids,
    (SELECT coalesce(jsonb_agg(id::text ORDER BY id), '[]'::jsonb) FROM pos.customers c JOIN params p ON p.tenant_id=c.tenant_id WHERE c.deleted_at IS NULL) AS customer_ids
), foreign_sample AS (
  SELECT
    (SELECT t.id::text FROM pos.tenants t JOIN params p ON t.id<>p.tenant_id ORDER BY t.created_at LIMIT 1) AS foreign_tenant_id,
    (SELECT s.id::text FROM pos.stores s JOIN params p ON s.tenant_id<>p.tenant_id AND s.deleted_at IS NULL ORDER BY s.created_at LIMIT 1) AS foreign_store_id,
    (SELECT u.id::text FROM pos.users u JOIN params p ON u.tenant_id<>p.tenant_id AND u.deleted_at IS NULL ORDER BY u.created_at LIMIT 1) AS foreign_user_id,
    (SELECT t.id::text FROM pos.terminals t JOIN params p ON t.tenant_id<>p.tenant_id AND t.deleted_at IS NULL ORDER BY t.created_at LIMIT 1) AS foreign_terminal_id,
    (SELECT c.id::text FROM pos.customers c JOIN params p ON c.tenant_id<>p.tenant_id AND c.deleted_at IS NULL ORDER BY c.created_at LIMIT 1) AS foreign_customer_id,
    (SELECT s.id::text FROM pos.sales s JOIN params p ON s.tenant_id<>p.tenant_id ORDER BY s.created_at LIMIT 1) AS foreign_sale_id,
    (SELECT pr.id::text FROM pos.products pr JOIN params p ON pr.tenant_id<>p.tenant_id AND pr.deleted_at IS NULL ORDER BY pr.created_at LIMIT 1) AS foreign_product_id,
    (SELECT ur.id::text FROM pos.update_releases ur JOIN params p ON ur.tenant_id IS NOT NULL AND ur.tenant_id<>p.tenant_id AND ur.revoked_at IS NULL ORDER BY ur.published_at DESC LIMIT 1) AS foreign_release_id
), isolation AS (
  SELECT
    (SELECT count(*) FROM pos.stores s JOIN params p ON s.tenant_id<>p.tenant_id)::int AS foreign_store_count,
    (SELECT count(*) FROM pos.users u JOIN params p ON u.tenant_id<>p.tenant_id)::int AS foreign_user_count,
    (SELECT count(*) FROM pos.terminals t JOIN params p ON t.tenant_id<>p.tenant_id)::int AS foreign_terminal_count,
    (SELECT count(*) FROM pos.customers c JOIN params p ON c.tenant_id<>p.tenant_id)::int AS foreign_customer_count,
    (SELECT count(*) FROM pos.products pr JOIN params p ON pr.tenant_id<>p.tenant_id)::int AS foreign_product_count
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT rt.ok THEN 'required_beta02_tables_missing' END,
    CASE WHEN NOT t.tenant_active THEN 'target_tenant_inactive' END,
    CASE WHEN t.tenant_config_count<>1 THEN 'tenant_config_invalid' END,
    CASE WHEN t.completed_bootstrap_run_count<1 THEN 'no_completed_production_bootstrap_run' END,
    CASE WHEN t.active_store_count<1 THEN 'no_active_store' END,
    CASE WHEN t.active_terminal_count<1 THEN 'no_active_terminal' END,
    CASE WHEN t.active_admin_count<>1 THEN 'admin_bootstrap_invalid' END,
    CASE WHEN t.seeded_role_count<>4 THEN 'mvp_role_seed_incomplete' END,
    CASE WHEN t.seeded_permission_assignment_count<1 THEN 'role_permission_seed_missing' END,
    CASE WHEN t.admin_owner_assignment_count<>1 THEN 'admin_owner_assignment_invalid' END,
    CASE WHEN t.admin_store_access_count<1 THEN 'admin_store_access_missing' END,
    CASE WHEN t.active_category_count<1 OR t.active_product_count<1 THEN 'tenant_catalog_seed_missing' END,
    CASE WHEN t.active_price_list_count<1 THEN 'tenant_price_list_missing' END,
    CASE WHEN t.tenant_release_count<1 THEN 'tenant_release_channel_missing' END,
    CASE WHEN f.foreign_tenant_id IS NULL THEN 'no_foreign_tenant_available_for_isolation_test' END,
    CASE WHEN i.foreign_store_count<1 OR i.foreign_user_count<1 OR i.foreign_terminal_count<1 OR i.foreign_product_count<1 THEN 'foreign_tenant_fixture_incomplete' END
  ],NULL) AS items
  FROM required_tables rt CROSS JOIN target t CROSS JOIN foreign_sample f CROSS JOIN isolation i
)
SELECT jsonb_build_object(
  'beta02SqlValidation', CASE WHEN cardinality((SELECT items FROM blockers))=0 THEN 'GO' ELSE 'NO-GO' END,
  'requiredTablesPresent',(SELECT ok FROM required_tables),
  'missingTables',(SELECT missing FROM required_tables),
  'tenantActive',(SELECT tenant_active FROM target),
  'tenantConfigCount',(SELECT tenant_config_count FROM target),
  'completedBootstrapRunCount',(SELECT completed_bootstrap_run_count FROM target),
  'activeStoreCount',(SELECT active_store_count FROM target),
  'activeTerminalCount',(SELECT active_terminal_count FROM target),
  'activeAdminCount',(SELECT active_admin_count FROM target),
  'seededRoleCount',(SELECT seeded_role_count FROM target),
  'seededPermissionAssignmentCount',(SELECT seeded_permission_assignment_count FROM target),
  'adminOwnerAssignmentCount',(SELECT admin_owner_assignment_count FROM target),
  'adminStoreAccessCount',(SELECT admin_store_access_count FROM target),
  'activeCategoryCount',(SELECT active_category_count FROM target),
  'activeProductCount',(SELECT active_product_count FROM target),
  'activePriceListCount',(SELECT active_price_list_count FROM target),
  'tenantReleaseCount',(SELECT tenant_release_count FROM target),
  'targetStoreIds',(SELECT store_ids FROM target),
  'targetUserIds',(SELECT user_ids FROM target),
  'targetTerminalIds',(SELECT terminal_ids FROM target),
  'targetCustomerIds',(SELECT customer_ids FROM target),
  'foreignTenantId',(SELECT foreign_tenant_id FROM foreign_sample),
  'foreignStoreId',(SELECT foreign_store_id FROM foreign_sample),
  'foreignUserId',(SELECT foreign_user_id FROM foreign_sample),
  'foreignTerminalId',(SELECT foreign_terminal_id FROM foreign_sample),
  'foreignCustomerId',(SELECT foreign_customer_id FROM foreign_sample),
  'foreignSaleId',(SELECT foreign_sale_id FROM foreign_sample),
  'foreignProductId',(SELECT foreign_product_id FROM foreign_sample),
  'foreignReleaseId',(SELECT foreign_release_id FROM foreign_sample),
  'blockers',(SELECT to_jsonb(items) FROM blockers),
  'schemaVersion',4,
  'syncContract','schema_version_4',
  'betaContract','tenant_provisioning_separation_hardening'
)::text;
