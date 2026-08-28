# BETA-01 — Controlled Commercial Beta Onboarding

## Objective
Onboard the first controlled commercial beta customer using explicit operational limits and production evidence without opening mass rollout.

## Required onboarding contract
BETA-01 validates the beta customer profile, store onboarding checklist, admin user bootstrap, catalog baseline, active MXN pricing, terminal assignment, support contact matrix, acceptance checklist, onboarding manifest, audit trail, release channel availability and SQL cross-check.

## Production evidence required
- `/health/live` = alive.
- `/health/ready` = ready and database ready.
- Admin login succeeds for the target tenant.
- `/api/v1/tenants/current` matches the requested tenant.
- Stores, users, roles, permissions, terminals and customers are readable under tenant-scoped authorization.
- Runtime catalog responds; SQL remains source of truth when runtime catalog visibility is partial.
- Update channels expose the controlled channels and include `beta`.
- Observability and audit endpoints return tenant-scoped evidence.
- SQL confirms active tenant, active store, active terminal assigned to an active store, active unlocked admin, admin role assignment, admin store access, active customer, catalog, pricing, tenant release and audit evidence.

## Safety invariants
- `schemaVersion = 4` and `syncContract = schema_version_4`.
- Inventory truth remains derived from `pos.inventory_ledger`.
- Modifier behavior remains `none | add | substitute`; substitute requires `replaces_product_id`.
- No negative price, invalid price window, invalid tax mode or invalid modifier behavior.
- No pending sync conflict or legacy schema event.
- Known retry/dead-letter conditions may remain only as documented non-blocking conditions.
- Never log passwords, JWT keys, database URLs, connection strings or API secrets.

## Exit decision
`PASS CONTROLLED COMMERCIAL BETA ONBOARDING / GO BETA-02`

Until the user runs the production validator successfully, repository delivery status is `PENDING USER VALIDATION`.
