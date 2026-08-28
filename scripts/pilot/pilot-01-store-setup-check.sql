\set ON_ERROR_STOP on
SET search_path TO pos, public;
SELECT set_config('app.tenant_id', :'tenant_id', true);

WITH params AS (
  SELECT
    :'tenant_id'::uuid AS tenant_id,
    :'store_code'::text AS store_code,
    :'admin_email'::text AS admin_email,
    :'product_sku'::text AS product_sku,
    :'payment_method_code'::text AS payment_method_code
),
facts AS (
  SELECT
    ps.tenant_id,
    (SELECT count(*) FROM pos.tenants t WHERE t.id = ps.tenant_id AND t.status = 'active' AND t.deleted_at IS NULL) AS active_tenant_count,
    (SELECT count(*) FROM pos.stores s WHERE s.tenant_id = ps.tenant_id AND s.code = ps.store_code AND s.status = 'active' AND s.deleted_at IS NULL) AS active_store_count,
    (SELECT count(*) FROM pos.users u WHERE u.tenant_id = ps.tenant_id AND u.email = ps.admin_email AND u.status = 'active' AND u.deleted_at IS NULL AND (u.locked_until IS NULL OR u.locked_until < now())) AS active_admin_count,
    (SELECT count(*) FROM pos.user_store_access usa JOIN pos.users u ON u.tenant_id = usa.tenant_id AND u.id = usa.user_id JOIN pos.stores s ON s.tenant_id = usa.tenant_id AND s.id = usa.store_id WHERE usa.tenant_id = ps.tenant_id AND u.email = ps.admin_email AND s.code = ps.store_code AND u.deleted_at IS NULL AND s.deleted_at IS NULL) AS admin_store_access_count,
    (SELECT count(*) FROM pos.products p WHERE p.tenant_id = ps.tenant_id AND p.sku = ps.product_sku AND p.status = 'active' AND p.is_sellable = true AND p.deleted_at IS NULL) AS active_product_count,
    (SELECT count(*) FROM pos.product_prices pp JOIN pos.products p ON p.tenant_id = pp.tenant_id AND p.id = pp.product_id WHERE pp.tenant_id = ps.tenant_id AND p.sku = ps.product_sku AND pp.price_cents > 0 AND pp.deleted_at IS NULL) AS active_product_price_count,
    (SELECT count(*) FROM pos.payment_methods pm WHERE pm.tenant_id = ps.tenant_id AND pm.code = ps.payment_method_code AND pm.method_type = 'cash' AND pm.status = 'active' AND pm.deleted_at IS NULL) AS active_cash_method_count,
    (SELECT count(*) FROM pos.terminals tr JOIN pos.stores s ON s.tenant_id = tr.tenant_id AND s.id = tr.store_id WHERE tr.tenant_id = ps.tenant_id AND s.code = ps.store_code AND tr.status = 'active' AND tr.deleted_at IS NULL) AS active_terminal_count,
    (SELECT count(*) FROM pos.sales sa WHERE sa.tenant_id = ps.tenant_id) AS sales_count,
    (SELECT count(*) FROM pos.audit_events ae WHERE ae.tenant_id = ps.tenant_id) AS audit_event_count
  FROM params ps
),
verdict AS (
  SELECT
    facts.*,
    (
      active_tenant_count = 1
      AND active_store_count >= 1
      AND active_admin_count = 1
      AND admin_store_access_count >= 1
      AND active_product_count >= 1
      AND active_product_price_count >= 1
      AND active_cash_method_count >= 1
      AND active_terminal_count >= 1
    ) AS is_go
  FROM facts
)
SELECT
  tenant_id,
  active_tenant_count,
  active_store_count,
  active_admin_count,
  admin_store_access_count,
  active_product_count,
  active_product_price_count,
  active_cash_method_count,
  active_terminal_count,
  sales_count,
  audit_event_count,
  CASE WHEN is_go THEN 'GO' ELSE 'NO-GO' END AS pilot_01_go_no_go
FROM verdict;

WITH params AS (
  SELECT
    :'tenant_id'::uuid AS tenant_id,
    :'store_code'::text AS store_code,
    :'admin_email'::text AS admin_email,
    :'product_sku'::text AS product_sku,
    :'payment_method_code'::text AS payment_method_code
),
facts AS (
  SELECT
    ps.tenant_id,
    (SELECT count(*) FROM pos.tenants t WHERE t.id = ps.tenant_id AND t.status = 'active' AND t.deleted_at IS NULL) AS active_tenant_count,
    (SELECT count(*) FROM pos.stores s WHERE s.tenant_id = ps.tenant_id AND s.code = ps.store_code AND s.status = 'active' AND s.deleted_at IS NULL) AS active_store_count,
    (SELECT count(*) FROM pos.users u WHERE u.tenant_id = ps.tenant_id AND u.email = ps.admin_email AND u.status = 'active' AND u.deleted_at IS NULL AND (u.locked_until IS NULL OR u.locked_until < now())) AS active_admin_count,
    (SELECT count(*) FROM pos.user_store_access usa JOIN pos.users u ON u.tenant_id = usa.tenant_id AND u.id = usa.user_id JOIN pos.stores s ON s.tenant_id = usa.tenant_id AND s.id = usa.store_id WHERE usa.tenant_id = ps.tenant_id AND u.email = ps.admin_email AND s.code = ps.store_code AND u.deleted_at IS NULL AND s.deleted_at IS NULL) AS admin_store_access_count,
    (SELECT count(*) FROM pos.products p WHERE p.tenant_id = ps.tenant_id AND p.sku = ps.product_sku AND p.status = 'active' AND p.is_sellable = true AND p.deleted_at IS NULL) AS active_product_count,
    (SELECT count(*) FROM pos.product_prices pp JOIN pos.products p ON p.tenant_id = pp.tenant_id AND p.id = pp.product_id WHERE pp.tenant_id = ps.tenant_id AND p.sku = ps.product_sku AND pp.price_cents > 0 AND pp.deleted_at IS NULL) AS active_product_price_count,
    (SELECT count(*) FROM pos.payment_methods pm WHERE pm.tenant_id = ps.tenant_id AND pm.code = ps.payment_method_code AND pm.method_type = 'cash' AND pm.status = 'active' AND pm.deleted_at IS NULL) AS active_cash_method_count,
    (SELECT count(*) FROM pos.terminals tr JOIN pos.stores s ON s.tenant_id = tr.tenant_id AND s.id = tr.store_id WHERE tr.tenant_id = ps.tenant_id AND s.code = ps.store_code AND tr.status = 'active' AND tr.deleted_at IS NULL) AS active_terminal_count
  FROM params ps
),
verdict AS (
  SELECT
    facts.*,
    (
      active_tenant_count = 1
      AND active_store_count >= 1
      AND active_admin_count = 1
      AND admin_store_access_count >= 1
      AND active_product_count >= 1
      AND active_product_price_count >= 1
      AND active_cash_method_count >= 1
      AND active_terminal_count >= 1
    ) AS is_go
  FROM facts
)
SELECT
  CASE
    WHEN is_go THEN 'PILOT-01 controlled store data setup PASS'
    ELSE ('PILOT-01 NO-GO tenant=' || active_tenant_count::text || ' store=' || active_store_count::text || ' admin=' || active_admin_count::text || ' store_access=' || admin_store_access_count::text || ' product=' || active_product_count::text || ' price=' || active_product_price_count::text || ' cash=' || active_cash_method_count::text || ' terminal=' || active_terminal_count::text)::integer::text
  END AS pilot_01_assertion
FROM verdict;
