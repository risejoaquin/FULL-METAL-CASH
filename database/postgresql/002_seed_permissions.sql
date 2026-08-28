BEGIN;

SET search_path TO pos, public;

DELETE FROM role_permissions WHERE permission_code = 'inventory.purchase';
DELETE FROM permissions WHERE code = 'inventory.purchase';

INSERT INTO permissions (code, description) VALUES
  ('tenant.manage', 'Manage tenant settings'),
  ('stores.manage', 'Manage stores'),
  ('users.manage', 'Manage users'),
  ('roles.manage', 'Manage roles and permissions'),
  ('terminals.manage', 'Manage terminals'),
  ('terminals.register', 'Register terminals'),
  ('catalog.read', 'Read catalog'),
  ('catalog.manage', 'Manage catalog'),
  ('customers.read', 'Read customers and customer purchase history'),
  ('customers.manage', 'Create and update customers'),
  ('discounts.read', 'Read discounts and promotions'),
  ('discounts.manage', 'Create and update discounts and promotions'),
  ('discounts.validate', 'Validate discounts before sales'),
  ('inventory.read', 'Read inventory'),
  ('inventory.adjust', 'Adjust inventory'),
  ('inventory.control', 'Manage inventory stock policies'),
  ('inventory.count', 'Create stock counts'),
  ('inventory.transfer', 'Create inventory transfers'),
  ('sales.read', 'Read sales and receipt read models'),
  ('sales.create', 'Create sales'),
  ('sales.void', 'Void sales'),
  ('returns.create', 'Create returns'),
  ('returns.read', 'Read returns and refunds'),
  ('cash.open', 'Open cash shift'),
  ('cash.close', 'Close cash shift'),
  ('cash.move', 'Create cash movements'),
  ('reports.read', 'Read reports'),
  ('reports.cash_shift_summary', 'Read operational cash-shift close summaries'),
  ('sync.push', 'Push terminal sync events'),
  ('sync.pull', 'Pull tenant sync changes'),
  ('sync.conflicts.read', 'Read sync conflicts and dead-letter diagnostics'),
  ('sync.conflicts.resolve', 'Resolve sync conflicts manually or by approved strategy'),
  ('builder.manage', 'Manage POS Builder'),
  ('updates.manage', 'Manage releases'),
  ('audit.read', 'Read audit events')
ON CONFLICT (code) DO UPDATE SET description = EXCLUDED.description;

COMMIT;
