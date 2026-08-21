# EXP-10 — Customer/Admin Management Completion

## Objective
Complete customer and admin management operational readiness for limited production expansion.

This phase validates that tenant administration no longer depends on manual SQL for basic operational tasks.

## Scope
- Customer create/list/get/update and sales history read model.
- Tenant current settings read contract.
- Store management list contract.
- User management create/update/list contract.
- Role and permission read contract.
- Terminal inventory/list contract.
- RBAC enforcement through protected endpoints.
- Audit trail for customer and user mutations.
- SQL cross-check for customer/admin state.

## Safety
EXP-10 creates one controlled customer and one controlled admin/support user for the tenant. It does not modify sales, payments, cash drawer, inventory ledger, sync events, stores, terminals, or update releases.

## Exit
EXP-10 exits with GO EXP-11 when build, tests, health, endpoint contracts, SQL cross-check, audit evidence, and decision matrix pass.
