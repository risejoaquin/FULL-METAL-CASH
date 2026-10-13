-- SolidPOS AC-DEV additive proposal. NEVER run on real production during development.
-- New recovery permissions remain unassigned until an authorized role administrator
-- explicitly grants them. Replacing seed_mvp_roles prevents the legacy "owner gets
-- every future permission" policy from silently granting recovery powers to new tenants.
BEGIN;
SET search_path TO pos, public;

INSERT INTO permissions(code, description) VALUES
  ('sync.recovery.execute', 'Execute approved scoped offline sync recovery'),
  ('sync.recovery.approve', 'Independently authorize scoped offline sync recovery')
ON CONFLICT (code) DO UPDATE SET description = EXCLUDED.description;

CREATE OR REPLACE FUNCTION pos.seed_mvp_roles(p_tenant_id uuid)
RETURNS void
LANGUAGE plpgsql
SET search_path = pos, public
AS $$
DECLARE
  owner_role_id uuid;
  admin_role_id uuid;
  manager_role_id uuid;
  cashier_role_id uuid;
BEGIN
  PERFORM set_config('app.tenant_id', p_tenant_id::text, true);

  INSERT INTO roles (tenant_id, code, name, is_system)
  VALUES
    (p_tenant_id, 'owner', 'Owner', true),
    (p_tenant_id, 'admin', 'Admin', true),
    (p_tenant_id, 'manager', 'Manager', true),
    (p_tenant_id, 'cashier', 'Cashier', true)
  ON CONFLICT (tenant_id, code)
  DO UPDATE SET
    name = EXCLUDED.name,
    is_system = EXCLUDED.is_system,
    updated_at = now();

  SELECT id INTO owner_role_id FROM roles WHERE tenant_id = p_tenant_id AND code = 'owner';
  SELECT id INTO admin_role_id FROM roles WHERE tenant_id = p_tenant_id AND code = 'admin';
  SELECT id INTO manager_role_id FROM roles WHERE tenant_id = p_tenant_id AND code = 'manager';
  SELECT id INTO cashier_role_id FROM roles WHERE tenant_id = p_tenant_id AND code = 'cashier';

  INSERT INTO role_permissions (tenant_id, role_id, permission_code)
  SELECT p_tenant_id, owner_role_id, code FROM permissions
  WHERE code NOT IN ('sync.recovery.execute', 'sync.recovery.approve')
  ON CONFLICT DO NOTHING;

  INSERT INTO role_permissions (tenant_id, role_id, permission_code)
  SELECT p_tenant_id, admin_role_id, code
  FROM permissions
  WHERE code IN (
    'tenant.manage',
    'stores.manage',
    'users.manage',
    'roles.manage',
    'terminals.manage',
    'terminals.register',
    'catalog.read',
    'catalog.manage',
    'customers.read',
    'customers.manage',
    'discounts.read',
    'discounts.manage',
    'discounts.validate',
    'inventory.read',
    'inventory.adjust',
    'inventory.control',
    'inventory.count',
    'inventory.transfer',
    'sales.read',
    'sales.create',
    'sales.void',
    'returns.create',
    'returns.read',
    'cash.open',
    'cash.close',
    'cash.move',
    'reports.read',
    'sync.pull',
    'sync.conflicts.read',
    'sync.conflicts.resolve',
    'audit.read',
    'builder.manage',
    'updates.manage'
  )
  ON CONFLICT DO NOTHING;

  INSERT INTO role_permissions (tenant_id, role_id, permission_code)
  SELECT p_tenant_id, manager_role_id, code
  FROM permissions
  WHERE code IN (
    'catalog.read',
    'customers.read',
    'customers.manage',
    'discounts.read',
    'discounts.manage',
    'discounts.validate',
    'inventory.read',
    'inventory.adjust',
    'inventory.control',
    'inventory.count',
    'inventory.transfer',
    'sales.read',
    'sales.create',
    'sales.void',
    'returns.create',
    'returns.read',
    'cash.open',
    'cash.close',
    'cash.move',
    'reports.read',
    'sync.pull',
    'sync.conflicts.read',
    'sync.conflicts.resolve',
    'builder.manage',
    'updates.manage'
  )
  ON CONFLICT DO NOTHING;

  INSERT INTO role_permissions (tenant_id, role_id, permission_code)
  SELECT p_tenant_id, cashier_role_id, code
  FROM permissions
  WHERE code IN (
    'catalog.read',
    'customers.read',
    'customers.manage',
    'discounts.read',
    'discounts.validate',
    'sales.read',
    'sales.create',
    'returns.create',
    'returns.read',
    'cash.open',
    'cash.close',
    'cash.move',
    'sync.push',
    'sync.pull',
    'sync.conflicts.read',
    'sync.conflicts.resolve'
  )
  ON CONFLICT DO NOTHING;
END;
$$;

-- Existing roles and role_permissions are intentionally unchanged.
-- New recovery permissions must be explicitly granted to TWO distinct users,
-- by a separately authorized administrator using existing RBAC management.
COMMIT;
