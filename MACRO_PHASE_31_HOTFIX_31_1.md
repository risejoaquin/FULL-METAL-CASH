# Macro Fase 31 Hotfix 31.1 — Admin Users SQL Read Fix

## Problem

`POST /api/v1/users` returned HTTP 500 during local validation.

Server log showed:

```text
Npgsql.PostgresException: 42601: syntax error at or near "u"
SolidPOS.PosServer.Infrastructure.AdminManagement.PostgreSqlAdminManagementRepository.ReadUsersAsync(...)
```

## Root cause

The user read model query reused a grouped JOIN-based SQL fragment. PostgreSQL rejected the composed SQL during the post-create readback path.

## Fix

Replaced the user read model SQL with a simpler outer `pos.users AS u` query and correlated subqueries for:

- role ids
- role codes
- store ids

This removes the fragile `GROUP BY`/multi-join aggregation from the runtime path used by:

- `GET /api/v1/users`
- `POST /api/v1/users`
- `PATCH /api/v1/users/{userId}`

## Files changed

```text
src/PosServer/SolidPOS.PosServer.Infrastructure/AdminManagement/PostgreSqlAdminManagementRepository.cs
MACRO_PHASE_31_HOTFIX_31_1.md
```

## Runtime scope

No database migration.
No contract changes.
No endpoint changes.

## Validation target

```text
POST /api/v1/users              PASS
PATCH /api/v1/users/{userId}    PASS
GET /api/v1/users               PASS
Auditoría user.created          PASS
Auditoría user.updated          PASS
```
