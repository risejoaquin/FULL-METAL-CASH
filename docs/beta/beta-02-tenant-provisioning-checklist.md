# BETA-02 Tenant Provisioning Checklist

- [ ] Provisioning status enabled.
- [ ] Provisioning bootstrap key configured server-side.
- [ ] Required header remains `X-SolidPOS-Provision-Key`.
- [ ] Completed bootstrap run exists for target tenant.
- [ ] Tenant config exists exactly once.
- [ ] owner/admin/manager/cashier roles seeded.
- [ ] Role-permission assignments present.
- [ ] Admin mapped to owner role.
- [ ] Admin has active store access.
- [ ] Tenant catalog baseline exists.
- [ ] Tenant price list exists.
- [ ] Tenant-scoped release exists.
- [ ] Idempotency request-hash mismatch is rejected by source hardening.
- [ ] Cross-tenant negative reads PASS.
- [ ] Store/user/terminal/customer list isolation PASS.
- [ ] SQL cross-check blockers empty.
