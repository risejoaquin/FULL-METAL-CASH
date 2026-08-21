\set ON_ERROR_STOP on

WITH params AS (
  SELECT
    :'tenant_id'::uuid AS tenant_id,
    :'category_id'::uuid AS category_id,
    :'product_id'::uuid AS product_id,
    :'variant_id'::uuid AS variant_id,
    :'barcode_id'::uuid AS barcode_id,
    :'price_id'::uuid AS price_id,
    :'price_list_id'::uuid AS price_list_id,
    :'modifier_group_id'::uuid AS modifier_group_id,
    :'modifier_id'::uuid AS modifier_id,
    :'sku'::text AS sku,
    :'barcode'::text AS barcode,
    :'expected_price_cents'::bigint AS expected_price_cents,
    now() AS checked_at
), table_status AS (
  SELECT bool_and(to_regclass(table_name) IS NOT NULL) AS required_tables_present,
         coalesce(jsonb_agg(table_name) FILTER (WHERE to_regclass(table_name) IS NULL), '[]'::jsonb) AS missing_tables
  FROM (VALUES
    ('pos.tenants'),('pos.categories'),('pos.products'),('pos.product_variants'),('pos.product_barcodes'),
    ('pos.price_lists'),('pos.product_prices'),('pos.modifier_groups'),('pos.modifiers'),
    ('pos.audit_events'),('pos.sync_changes')
  ) required(table_name)
), topology AS (
  SELECT
    EXISTS (SELECT 1 FROM pos.tenants t JOIN params p ON p.tenant_id=t.id WHERE t.status='active' AND t.deleted_at IS NULL) AS tenant_active,
    EXISTS (SELECT 1 FROM pos.price_lists pl JOIN params p ON p.tenant_id=pl.tenant_id AND p.price_list_id=pl.id WHERE pl.status='active' AND pl.deleted_at IS NULL) AS price_list_active
), catalog_row AS (
  SELECT
    EXISTS (SELECT 1 FROM pos.categories c JOIN params p ON p.tenant_id=c.tenant_id AND p.category_id=c.id WHERE c.status='active' AND c.deleted_at IS NULL) AS category_exists,
    EXISTS (SELECT 1 FROM pos.products pr JOIN params p ON p.tenant_id=pr.tenant_id AND p.product_id=pr.id WHERE pr.sku=p.sku AND pr.status='active' AND pr.is_sellable=true AND pr.deleted_at IS NULL) AS product_exists,
    EXISTS (SELECT 1 FROM pos.product_variants v JOIN params p ON p.tenant_id=v.tenant_id AND p.variant_id=v.id AND p.product_id=v.product_id WHERE v.status='active' AND v.deleted_at IS NULL) AS variant_exists,
    EXISTS (SELECT 1 FROM pos.product_barcodes b JOIN params p ON p.tenant_id=b.tenant_id AND p.barcode_id=b.id AND p.product_id=b.product_id WHERE b.barcode=p.barcode AND b.deleted_at IS NULL) AS barcode_exists,
    EXISTS (SELECT 1 FROM pos.product_prices pp JOIN params p ON p.tenant_id=pp.tenant_id AND p.price_id=pp.id AND p.product_id=pp.product_id AND p.price_list_id=pp.price_list_id WHERE pp.price_cents=p.expected_price_cents AND pp.currency='MXN' AND pp.deleted_at IS NULL AND (pp.starts_at IS NULL OR pp.starts_at <= now()) AND (pp.ends_at IS NULL OR pp.ends_at > now())) AS active_price_exists,
    EXISTS (SELECT 1 FROM pos.modifier_groups mg JOIN params p ON p.tenant_id=mg.tenant_id AND p.modifier_group_id=mg.id WHERE mg.deleted_at IS NULL) AS modifier_group_exists,
    EXISTS (SELECT 1 FROM pos.modifiers m JOIN params p ON p.tenant_id=m.tenant_id AND p.modifier_id=m.id AND p.modifier_group_id=m.group_id WHERE m.inventory_behavior='none' AND m.deleted_at IS NULL) AS modifier_exists
), counts AS (
  SELECT
    (SELECT COUNT(*) FROM pos.categories c JOIN params p ON p.tenant_id=c.tenant_id WHERE c.deleted_at IS NULL)::int AS category_count,
    (SELECT COUNT(*) FROM pos.products pr JOIN params p ON p.tenant_id=pr.tenant_id WHERE pr.deleted_at IS NULL)::int AS product_count,
    (SELECT COUNT(*) FROM pos.products pr JOIN params p ON p.tenant_id=pr.tenant_id WHERE pr.status='active' AND pr.is_sellable=true AND pr.deleted_at IS NULL)::int AS active_sellable_product_count,
    (SELECT COUNT(*) FROM pos.product_prices pp JOIN params p ON p.tenant_id=pp.tenant_id WHERE pp.deleted_at IS NULL)::int AS product_price_count,
    (SELECT COUNT(*) FROM pos.product_prices pp JOIN params p ON p.tenant_id=pp.tenant_id WHERE pp.deleted_at IS NULL AND pp.price_cents < 0)::int AS negative_price_count,
    (SELECT COUNT(*) FROM pos.product_prices pp JOIN params p ON p.tenant_id=pp.tenant_id WHERE pp.deleted_at IS NULL AND pp.ends_at IS NOT NULL AND pp.starts_at IS NOT NULL AND pp.ends_at <= pp.starts_at)::int AS invalid_price_window_count,
    (SELECT COUNT(*) FROM pos.products pr JOIN params p ON p.tenant_id=pr.tenant_id WHERE pr.deleted_at IS NULL AND pr.tax_mode NOT IN ('taxable','exempt'))::int AS invalid_tax_mode_count,
    (SELECT COUNT(*) FROM pos.modifiers m JOIN params p ON p.tenant_id=m.tenant_id WHERE m.deleted_at IS NULL AND m.inventory_behavior NOT IN ('none','add','substitute'))::int AS invalid_modifier_behavior_count,
    (SELECT COUNT(*) FROM pos.modifiers m JOIN params p ON p.tenant_id=m.tenant_id WHERE m.deleted_at IS NULL AND m.inventory_behavior='substitute' AND m.replaces_product_id IS NULL)::int AS invalid_substitute_modifier_count,
    (SELECT COUNT(*) FROM pos.sync_changes sc JOIN params p ON p.tenant_id=sc.tenant_id WHERE sc.entity_type='tenant.catalog' AND sc.entity_id IN (p.category_id,p.product_id,p.variant_id,p.barcode_id,p.modifier_group_id,p.modifier_id))::int AS exp11_catalog_sync_change_count,
    (SELECT COUNT(*) FROM pos.sync_changes sc JOIN params p ON p.tenant_id=sc.tenant_id WHERE sc.entity_type='price.updated' AND sc.entity_id=p.price_id)::int AS exp11_price_sync_change_count,
    (SELECT COUNT(*) FROM pos.audit_events ae JOIN params p ON p.tenant_id=ae.tenant_id WHERE ae.entity_id::text IN (p.category_id::text,p.product_id::text,p.variant_id::text,p.barcode_id::text,p.price_id::text,p.modifier_group_id::text,p.modifier_id::text) AND ae.action IN ('admin.catalog.category.upsert','admin.catalog.product.upsert','admin.catalog.variant.upsert','admin.catalog.barcode.upsert','admin.catalog.price.upsert','admin.catalog.modifier_group.upsert','admin.catalog.modifier.upsert'))::int AS exp11_audit_event_count,
    (SELECT COUNT(*) FROM pos.audit_events ae JOIN params p ON p.tenant_id=ae.tenant_id WHERE ae.occurred_at >= now() - interval '24 hours')::int AS audit_events_last_24_hours
), blockers AS (
  SELECT array_remove(ARRAY[
    CASE WHEN NOT ts.required_tables_present THEN 'required_catalog_tables_missing' END,
    CASE WHEN NOT topo.tenant_active THEN 'tenant_missing_or_inactive' END,
    CASE WHEN NOT topo.price_list_active THEN 'price_list_missing_or_inactive' END,
    CASE WHEN NOT cr.category_exists THEN 'exp11_category_missing' END,
    CASE WHEN NOT cr.product_exists THEN 'exp11_product_missing' END,
    CASE WHEN NOT cr.variant_exists THEN 'exp11_variant_missing' END,
    CASE WHEN NOT cr.barcode_exists THEN 'exp11_barcode_missing' END,
    CASE WHEN NOT cr.active_price_exists THEN 'exp11_active_price_missing' END,
    CASE WHEN NOT cr.modifier_group_exists THEN 'exp11_modifier_group_missing' END,
    CASE WHEN NOT cr.modifier_exists THEN 'exp11_modifier_missing' END,
    CASE WHEN c.negative_price_count > 0 THEN 'negative_product_price_detected' END,
    CASE WHEN c.invalid_price_window_count > 0 THEN 'invalid_price_window_detected' END,
    CASE WHEN c.invalid_tax_mode_count > 0 THEN 'invalid_tax_mode_detected' END,
    CASE WHEN c.invalid_modifier_behavior_count > 0 THEN 'invalid_modifier_behavior_detected' END,
    CASE WHEN c.invalid_substitute_modifier_count > 0 THEN 'invalid_substitute_modifier_detected' END,
    CASE WHEN c.exp11_catalog_sync_change_count < 6 THEN 'exp11_catalog_sync_evidence_missing' END,
    CASE WHEN c.exp11_price_sync_change_count < 1 THEN 'exp11_price_sync_evidence_missing' END,
    CASE WHEN c.exp11_audit_event_count < 7 THEN 'exp11_catalog_audit_evidence_missing' END
  ], NULL) AS items
  FROM table_status ts CROSS JOIN topology topo CROSS JOIN catalog_row cr CROSS JOIN counts c
), warnings AS (
  SELECT array_remove(ARRAY[
    CASE WHEN c.category_count < 1 THEN 'catalog_categories_empty' END,
    CASE WHEN c.active_sellable_product_count < 1 THEN 'active_sellable_catalog_empty' END,
    CASE WHEN c.product_price_count < 1 THEN 'product_prices_empty' END
  ], NULL) AS items
  FROM counts c
)
SELECT jsonb_build_object(
  'exp11SqlValidation', CASE WHEN cardinality((SELECT items FROM blockers)) = 0 THEN 'GO' ELSE 'NO-GO' END,
  'requiredTablesPresent', (SELECT required_tables_present FROM table_status),
  'missingTables', (SELECT missing_tables FROM table_status),
  'tenantActive', (SELECT tenant_active FROM topology),
  'priceListActive', (SELECT price_list_active FROM topology),
  'categoryExists', (SELECT category_exists FROM catalog_row),
  'productExists', (SELECT product_exists FROM catalog_row),
  'variantExists', (SELECT variant_exists FROM catalog_row),
  'barcodeExists', (SELECT barcode_exists FROM catalog_row),
  'activePriceExists', (SELECT active_price_exists FROM catalog_row),
  'modifierGroupExists', (SELECT modifier_group_exists FROM catalog_row),
  'modifierExists', (SELECT modifier_exists FROM catalog_row),
  'categoryCount', (SELECT category_count FROM counts),
  'productCount', (SELECT product_count FROM counts),
  'activeSellableProductCount', (SELECT active_sellable_product_count FROM counts),
  'productPriceCount', (SELECT product_price_count FROM counts),
  'negativePriceCount', (SELECT negative_price_count FROM counts),
  'invalidPriceWindowCount', (SELECT invalid_price_window_count FROM counts),
  'invalidTaxModeCount', (SELECT invalid_tax_mode_count FROM counts),
  'invalidModifierBehaviorCount', (SELECT invalid_modifier_behavior_count FROM counts),
  'invalidSubstituteModifierCount', (SELECT invalid_substitute_modifier_count FROM counts),
  'exp11CatalogSyncChangeCount', (SELECT exp11_catalog_sync_change_count FROM counts),
  'exp11PriceSyncChangeCount', (SELECT exp11_price_sync_change_count FROM counts),
  'exp11AuditEventCount', (SELECT exp11_audit_event_count FROM counts),
  'auditEventsLast24Hours', (SELECT audit_events_last_24_hours FROM counts),
  'blockers', (SELECT to_jsonb(items) FROM blockers),
  'sqlWarnings', (SELECT to_jsonb(items) FROM warnings),
  'schemaVersion', 4,
  'catalogPricingContract', 'catalog_pricing_operations'
)::text;
