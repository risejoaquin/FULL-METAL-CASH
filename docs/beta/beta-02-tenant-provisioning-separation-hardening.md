# BETA-02 — Beta Tenant Provisioning and Separation Hardening

## Objective
Prove that production tenant provisioning is repeatable, tenant-scoped, RBAC-seeded and resistant to cross-tenant leakage before onboarding more beta customers.

## Hardening introduced
- Existing provisioning idempotency keys now validate the stored `request_hash` against the incoming non-secret provisioning payload before replaying an existing result.
- A changed payload using an already completed idempotency key is rejected instead of being silently treated as the original request.
- The request fingerprint covers tenant/store/admin identity and non-secret tenant configuration inputs while excluding the raw admin password.
- A PostgreSQL integration regression test covers the mismatched idempotency-payload family and verifies no second tenant/bootstrap run is created.

## Production validation contract
BETA-02 validates:
- `/api/v1/provisioning/status` enabled/configured with `X-SolidPOS-Provision-Key` contract.
- Admin login and current tenant context.
- Completed production bootstrap evidence.
- Exactly the four MVP roles: owner, admin, manager and cashier.
- Role-permission assignments, owner assignment and store access for the admin.
- Active store, terminal, catalog seed, price list and tenant release.
- List isolation for stores, users, terminals and customers by cross-checking every API id against SQL ownership.
- Runtime catalog contains no sampled foreign product id.
- Direct reads of a foreign customer and foreign sale return 404 when fixtures exist.

## Safety
The validator never accepts or prints the provisioning key. It validates production provisioning configuration without creating a new tenant. No destructive cross-tenant mutation is used for leakage testing.

`schemaVersion = 4` and `syncContract = schema_version_4` remain mandatory.

## Exit
`PASS BETA TENANT PROVISIONING SEPARATION HARDENING / GO BETA-03`

Repository delivery state before user execution: `PENDING USER VALIDATION`.
