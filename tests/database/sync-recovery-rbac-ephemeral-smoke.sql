-- Test-only SQL for PostgreSQL 16 on the ephemeral GitHub Actions service.
-- Never run against a pre-existing database. Requires 001, 002, 003 and the unapproved role-recovery SQL proposal.
SET search_path TO pos, public;

DO $$
DECLARE
    old_tenant uuid := '9b6619c1-f236-47c7-bcd8-2cfe15654321';
    new_tenant uuid := '9b6619c1-f236-47c7-bcd8-2cfe15654322';
    owner_id uuid;
    admin_id uuid;
BEGIN
    IF (SELECT COUNT(*) FROM permissions
        WHERE code IN ('sync.recovery.execute', 'sync.recovery.approve')) <> 2 THEN
        RAISE EXCEPTION 'New permission codes missing';
    END IF;

    -- The old tenant was created and seeded prior to 022; check it was
    -- neither modified nor implicitly granted either elevated permission.
    SELECT r.id INTO owner_id FROM roles r
    WHERE r.tenant_id = old_tenant AND r.code = 'owner';

    IF owner_id IS NULL THEN
        RAISE EXCEPTION 'Legacy owner role is missing';
    END IF;

    IF EXISTS (SELECT 1 FROM role_permissions rp
       WHERE rp.role_id = owner_id AND rp.tenant_id = old_tenant
         AND rp.permission_code IN ('sync.recovery.execute','sync.recovery.approve')) THEN
        RAISE EXCEPTION 'Migration implicitly modified existing owner permissions';
    END IF;

    -- Even re-seeding a preexisting tenant must not silently grant them.
    PERFORM pos.seed_mvp_roles(old_tenant);
    IF EXISTS (SELECT 1 FROM role_permissions rp
       WHERE rp.role_id = owner_id AND rp.tenant_id = old_tenant
         AND rp.permission_code IN ('sync.recovery.execute','sync.recovery.approve')) THEN
        RAISE EXCEPTION 'Reseed silently expanded existing owner privileges';
    END IF;

    -- Fresh tenant provisioned after 022 must also be least-privilege.
    INSERT INTO tenants (id, name) VALUES (new_tenant, 'RBAC test new tenant');
    PERFORM pos.seed_mvp_roles(new_tenant);
    IF EXISTS (SELECT 1 FROM role_permissions rp
       WHERE rp.tenant_id = new_tenant
         AND rp.permission_code IN ('sync.recovery.execute','sync.recovery.approve')) THEN
        RAISE EXCEPTION 'New tenant inherited recovery permission by default';
    END IF;

    SELECT r.id INTO admin_id FROM roles r
    WHERE r.tenant_id = new_tenant AND r.code = 'admin';
    IF admin_id IS NULL THEN
        RAISE EXCEPTION 'New tenant admin role is missing';
    END IF;

    -- An authorized RBAC administrator can explicitly grant a permission
    -- to a role. The independent approval permission remains absent.
    INSERT INTO role_permissions (tenant_id, role_id, permission_code)
    VALUES (new_tenant, admin_id, 'sync.recovery.execute');

    PERFORM pos.seed_mvp_roles(new_tenant);
    IF (SELECT COUNT(*) FROM role_permissions rp
        WHERE rp.tenant_id = new_tenant
          AND rp.role_id = admin_id
          AND rp.permission_code = 'sync.recovery.execute') <> 1 THEN
        RAISE EXCEPTION 'Explicit grant did not survive idempotent role seeding';
    END IF;
    IF EXISTS (SELECT 1 FROM role_permissions rp
       WHERE rp.tenant_id = new_tenant
         AND rp.permission_code = 'sync.recovery.approve') THEN
        RAISE EXCEPTION 'Independent approval was implicitly granted';
    END IF;
END;
$$;

SELECT 'PASS: explicit recovery permissions and seed hardening on disposable PostgreSQL' AS result;
