\set ON_ERROR_STOP on

WITH params AS (
  SELECT :'tenant_id'::uuid AS tenant_id
), existing AS (
  SELECT id, code, name, currency::text, false AS created
  FROM pos.price_lists pl
  JOIN params p ON p.tenant_id = pl.tenant_id
  WHERE pl.deleted_at IS NULL
    AND pl.status = 'active'
    AND pl.currency = 'MXN'
  ORDER BY CASE WHEN pl.code IN ('DEFAULT','MAIN','MXN','EXP11-MXN') THEN 0 ELSE 1 END, pl.code
  LIMIT 1
), inserted AS (
  INSERT INTO pos.price_lists (id, tenant_id, code, name, currency, status, created_at, updated_at)
  SELECT gen_random_uuid(), p.tenant_id, 'EXP11-MXN', 'EXP-11 Controlled MXN Price List', 'MXN', 'active', now(), now()
  FROM params p
  WHERE NOT EXISTS (SELECT 1 FROM existing)
    AND NOT EXISTS (
      SELECT 1 FROM pos.price_lists pl
      WHERE pl.tenant_id = p.tenant_id
        AND pl.code = 'EXP11-MXN'
        AND pl.deleted_at IS NULL
    )
  RETURNING id, code, name, currency::text, true AS created
), revived AS (
  UPDATE pos.price_lists pl
  SET status = 'active', currency = 'MXN', updated_at = now()
  FROM params p
  WHERE pl.tenant_id = p.tenant_id
    AND pl.code = 'EXP11-MXN'
    AND pl.deleted_at IS NULL
    AND NOT EXISTS (SELECT 1 FROM existing)
    AND NOT EXISTS (SELECT 1 FROM inserted)
  RETURNING pl.id, pl.code, pl.name, pl.currency::text, false AS created
), selected AS (
  SELECT * FROM existing
  UNION ALL
  SELECT * FROM inserted
  UNION ALL
  SELECT * FROM revived
  LIMIT 1
)
SELECT jsonb_build_object(
  'priceListId', (SELECT id FROM selected),
  'code', (SELECT code FROM selected),
  'name', (SELECT name FROM selected),
  'currency', (SELECT currency FROM selected),
  'created', coalesce((SELECT created FROM selected), false),
  'contract', 'exp_11_controlled_price_list_bootstrap'
)::text;
