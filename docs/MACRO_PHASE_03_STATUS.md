# Macro Phase 03 Status - Tenant Context + Auth/RBAC Base

## Goal

Implement authentication and authorization foundations before business modules.

## Implemented

- JWT options.
- Password hashing options.
- BCrypt password hasher.
- JWT access token service.
- Refresh token generation and SHA-256 hashing.
- Auth repository with explicit PostgreSQL SQL.
- Login scaffold.
- Refresh-token rotation base.
- Logout refresh-token revocation.
- Required claims middleware.
- Tenant context reads authenticated claims.
- Permission authorization requirement.
- Permission policies generated from `PermissionCodes`.
- `/api/v1/auth/login`.
- `/api/v1/auth/refresh`.
- `/api/v1/auth/logout`.
- Unit tests for JWT required claims.
- Unit tests for BCrypt roundtrip.

## Security Notes

- Login failures return generic `Invalid credentials`.
- User enumeration is avoided by generic response and dummy BCrypt verification.
- Access tokens must include:
  - `tenant_id`
  - `user_id` or `terminal_id`
- Logs must never include:
  - passwords
  - access tokens
  - refresh tokens
  - enrollment tokens
  - device tokens

## Required Configuration

Development uses:

```json
"Jwt": {
  "SigningKey": "dev-only-solidpos-signing-key-change-before-production"
}
```

Production must provide:

```text
Jwt__SigningKey=<strong secret>
```

## Current Limit

The auth endpoints are implemented, but there is not yet a tenant/user provisioning endpoint.

To test login manually, a tenant, user, roles and role assignments must exist in PostgreSQL.

Provisioning will be formalized in the next macro phases.
