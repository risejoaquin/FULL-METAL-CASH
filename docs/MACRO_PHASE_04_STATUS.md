# Macro Phase 04 Status - Terminal Enrollment + Tenant Runtime

## Goal

Bind physical POS terminals to a tenant/store and make terminal JWTs operational.

## Implemented

- Terminal enrollment token creation.
- Anonymous terminal registration with enrollment token.
- Terminal JWT with:
  - `tenant_id`
  - `store_id`
  - `terminal_id`
  - terminal permissions
- Terminal revocation.
- Runtime validation that terminal JWTs still map to an active terminal.
- Runtime terminal context endpoint protected by `sync.pull`.
- `ITenantContext.StoreId`.
- Request log enrichment with `store_id`.
- PostgreSQL tenant session helper that sets `app.tenant_id`.
- Protected terminal management endpoints using real RBAC policies.
- OpenAPI contract additions.
- JWT unit test for terminal claims.
- Integration test assertion that excluded MVP permission `inventory.purchase` is not seeded.

## Endpoints

- `POST /api/v1/terminals/enrollment-token`
- `POST /api/v1/auth/terminal/register`
- `GET /api/v1/terminals`
- `POST /api/v1/terminals/{terminalId}/revoke`
- `GET /api/v1/terminal/session`

## Security Notes

- Enrollment tokens are returned only once and stored as SHA-256 hashes.
- Terminal JWTs are rejected if the terminal is blocked, retired, deleted or hard locked.
- Revocation is enforced at runtime even before JWT expiration.
- Terminal tokens do not include `user_id`.
- User tokens do not include `terminal_id`.

## Current Limit

Enrollment token provisioning is available through API, but there is not yet a dashboard UI.

Terminal heartbeat and sync endpoints will be implemented in later macro phases.
