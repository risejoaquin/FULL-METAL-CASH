\set ON_ERROR_STOP on
SELECT set_config('app.tenant_id', :'tenant_id', false);

WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id,
         lower(:'admin_email'::text) AS admin_email
), tenant_tables AS (
  SELECT c.table_name,
         pc.relrowsecurity,
         EXISTS (
           SELECT 1 FROM pg_policies p
           WHERE p.schemaname='pos' AND p.tablename=c.table_name
         ) AS has_policy
  FROM information_schema.columns c
  JOIN pg_class pc ON pc.relname=c.table_name
  JOIN pg_namespace pn ON pn.oid=pc.relnamespace AND pn.nspname=c.table_schema
  WHERE c.table_schema='pos'
    AND c.column_name='tenant_id'
    AND pc.relkind='r'
), rls AS (
  SELECT
    count(*)::int AS tenant_table_count,
    count(*) FILTER (WHERE relrowsecurity)::int AS rls_enabled_count,
    count(*) FILTER (WHERE NOT relrowsecurity)::int AS rls_missing_count,
    count(*) FILTER (WHERE has_policy)::int AS policy_present_count,
    count(*) FILTER (WHERE NOT has_policy)::int AS policy_missing_count,
    coalesce(jsonb_agg(table_name ORDER BY table_name) FILTER (WHERE NOT relrowsecurity),'[]'::jsonb) AS tables_without_rls,
    coalesce(jsonb_agg(table_name ORDER BY table_name) FILTER (WHERE NOT has_policy),'[]'::jsonb) AS tables_without_policy
  FROM tenant_tables
), target AS (
  SELECT
    EXISTS (SELECT 1 FROM pos.tenants t JOIN params p ON p.tenant_id=t.id WHERE t.status='active' AND t.deleted_at IS NULL) AS tenant_active,
    (SELECT count(*)::int FROM pos.users u JOIN params p ON p.tenant_id=u.tenant_id WHERE lower(u.email)=p.admin_email AND u.status='active' AND u.deleted_at IS NULL) AS active_admin_count,
    (SELECT count(*)::int FROM pos.user_roles ur JOIN params p ON p.tenant_id=ur.tenant_id) AS user_role_count,
    (SELECT count(*)::int FROM pos.role_permissions rp JOIN params p ON p.tenant_id=rp.tenant_id) AS role_permission_count,
    (SELECT count(*)::int FROM pos.user_store_access usa JOIN params p ON p.tenant_id=usa.tenant_id) AS store_access_count,
    (SELECT count(*)::int FROM pos.refresh_tokens rt JOIN params p ON p.tenant_id=rt.tenant_id WHERE rt.token_hash IS NULL OR rt.token_hash !~ '^[0-9A-F]{64}$') AS invalid_refresh_hash_count,
    (SELECT count(*)::int FROM pos.users u JOIN params p ON p.tenant_id=u.tenant_id WHERE u.password_hash IS NULL OR u.password_hash !~ '^\$2[aby]\$[0-9]{2}\$') AS invalid_password_hash_count,
    (SELECT count(*)::int FROM pos.production_bootstrap_runs r JOIN params p ON p.tenant_id=r.tenant_id AND r.status='completed') AS completed_bootstrap_run_count,
    (SELECT count(*)::int FROM pos.audit_events a JOIN params p ON p.tenant_id=a.tenant_id) AS audit_event_count,
    (SELECT count(*)::int FROM pos.update_releases ur JOIN params p ON ur.tenant_id=p.tenant_id AND ur.revoked_at IS NULL) AS tenant_release_count,
    (SELECT count(*)::int FROM pos.return_refunds rr JOIN params p ON rr.tenant_id=p.tenant_id) AS refund_count,
    (SELECT count(*)::int FROM pos.background_jobs bj JOIN params p ON bj.tenant_id=p.tenant_id) AS background_job_count
), foreign_sample AS (
  SELECT
    (SELECT t.id::text FROM pos.tenants t JOIN params p ON t.id<>p.tenant_id ORDER BY t.created_at LIMIT 1) AS foreign_tenant_id,
    (SELECT c.id::text FROM pos.customers c JOIN params p ON c.tenant_id<>p.tenant_id AND c.deleted_at IS NULL ORDER BY c.created_at LIMIT 1) AS foreign_customer_id,
    (SELECT s.id::text FROM pos.sales s JOIN params p ON s.tenant_id<>p.tenant_id ORDER BY s.created_at LIMIT 1) AS foreign_sale_id,
    (SELECT u.id::text FROM pos.users u JOIN params p ON u.tenant_id<>p.tenant_id AND u.deleted_at IS NULL ORDER BY u.created_at LIMIT 1) AS foreign_user_id,
    (SELECT st.id::text FROM pos.stores st JOIN params p ON st.tenant_id<>p.tenant_id AND st.deleted_at IS NULL ORDER BY st.created_at LIMIT 1) AS foreign_store_id,
    (SELECT pr.id::text FROM pos.products pr JOIN params p ON pr.tenant_id<>p.tenant_id AND pr.deleted_at IS NULL ORDER BY pr.created_at LIMIT 1) AS foreign_product_id,
    (SELECT ur.id::text FROM pos.update_releases ur JOIN params p ON ur.tenant_id IS NOT NULL AND ur.tenant_id<>p.tenant_id ORDER BY ur.published_at DESC LIMIT 1) AS foreign_release_id
), constraints AS (
  SELECT
    EXISTS (
      SELECT 1 FROM pg_indexes
      WHERE schemaname='pos' AND tablename='production_bootstrap_runs'
        AND indexdef ILIKE '%tenant_id%'
    ) AS bootstrap_index_present,
    EXISTS (
      SELECT 1 FROM pg_policies WHERE schemaname='pos' AND tablename='return_refunds'
    ) AS refund_rls_policy_present,
    EXISTS (
      SELECT 1 FROM pg_policies WHERE schemaname='pos' AND tablename='update_releases'
    ) AS release_rls_policy_present,
    EXISTS (
      SELECT 1 FROM pg_policies WHERE schemaname='pos' AND tablename='background_jobs'
    ) AS background_job_rls_policy_present
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT t.tenant_active THEN 'target_tenant_inactive' END,
    CASE WHEN t.active_admin_count<>1 THEN 'production_admin_identity_invalid' END,
    CASE WHEN t.user_role_count<1 THEN 'rbac_user_roles_missing' END,
    CASE WHEN t.role_permission_count<1 THEN 'rbac_role_permissions_missing' END,
    CASE WHEN t.store_access_count<1 THEN 'store_access_assignments_missing' END,
    CASE WHEN t.invalid_refresh_hash_count<>0 THEN 'invalid_refresh_token_hash_storage' END,
    CASE WHEN t.invalid_password_hash_count<>0 THEN 'invalid_password_hash_storage' END,
    CASE WHEN t.completed_bootstrap_run_count<1 THEN 'production_bootstrap_evidence_missing' END,
    CASE WHEN t.audit_event_count<1 THEN 'audit_evidence_missing' END,
    CASE WHEN r.rls_missing_count<>0 THEN 'tenant_table_rls_missing' END,
    CASE WHEN r.policy_missing_count<>0 THEN 'tenant_table_policy_missing' END,
    CASE WHEN NOT c.refund_rls_policy_present THEN 'return_refunds_rls_policy_missing' END,
    CASE WHEN NOT c.release_rls_policy_present THEN 'update_releases_rls_policy_missing' END,
    CASE WHEN NOT c.background_job_rls_policy_present THEN 'background_jobs_rls_policy_missing' END,
    CASE WHEN f.foreign_tenant_id IS NULL THEN 'foreign_tenant_fixture_missing' END
  ],NULL) AS items
  FROM target t CROSS JOIN rls r CROSS JOIN constraints c CROSS JOIN foreign_sample f
)
SELECT jsonb_build_object(
  'ga08SqlDecision',CASE WHEN cardinality((SELECT items FROM blockers))=0 THEN 'GO' ELSE 'NO-GO' END,
  'tenantActive',(SELECT tenant_active FROM target),
  'activeAdminCount',(SELECT active_admin_count FROM target),
  'userRoleCount',(SELECT user_role_count FROM target),
  'rolePermissionCount',(SELECT role_permission_count FROM target),
  'storeAccessCount',(SELECT store_access_count FROM target),
  'invalidRefreshHashCount',(SELECT invalid_refresh_hash_count FROM target),
  'invalidPasswordHashCount',(SELECT invalid_password_hash_count FROM target),
  'completedBootstrapRunCount',(SELECT completed_bootstrap_run_count FROM target),
  'auditEventCount',(SELECT audit_event_count FROM target),
  'tenantReleaseCount',(SELECT tenant_release_count FROM target),
  'refundCount',(SELECT refund_count FROM target),
  'backgroundJobCount',(SELECT background_job_count FROM target),
  'tenantTableCount',(SELECT tenant_table_count FROM rls),
  'rlsEnabledTenantTableCount',(SELECT rls_enabled_count FROM rls),
  'rlsMissingTenantTableCount',(SELECT rls_missing_count FROM rls),
  'rlsPolicyPresentTenantTableCount',(SELECT policy_present_count FROM rls),
  'rlsPolicyMissingTenantTableCount',(SELECT policy_missing_count FROM rls),
  'tablesWithoutRls',(SELECT tables_without_rls FROM rls),
  'tablesWithoutPolicy',(SELECT tables_without_policy FROM rls),
  'returnRefundsRlsPolicyPresent',(SELECT refund_rls_policy_present FROM constraints),
  'updateReleasesRlsPolicyPresent',(SELECT release_rls_policy_present FROM constraints),
  'backgroundJobsRlsPolicyPresent',(SELECT background_job_rls_policy_present FROM constraints),
  'foreignTenantId',(SELECT foreign_tenant_id FROM foreign_sample),
  'foreignCustomerId',(SELECT foreign_customer_id FROM foreign_sample),
  'foreignSaleId',(SELECT foreign_sale_id FROM foreign_sample),
  'foreignUserId',(SELECT foreign_user_id FROM foreign_sample),
  'foreignStoreId',(SELECT foreign_store_id FROM foreign_sample),
  'foreignProductId',(SELECT foreign_product_id FROM foreign_sample),
  'foreignReleaseId',(SELECT foreign_release_id FROM foreign_sample),
  'blockers',(SELECT to_jsonb(items) FROM blockers),
  'schemaVersion',4,
  'syncContract','schema_version_4',
  'generalAvailabilityActivated',false
)::text;
