# Macro Fase 31 Hotfix 31.2 — Users Read SQL Whitespace Fix

## Status
IMPLEMENTED — pending local validation

## Cause
`POST /api/v1/users` and `GET /api/v1/users` failed with PostgreSQL syntax error `42601: syntax error at or near "u"` inside `PostgreSqlAdminManagementRepository.ReadUsersAsync`.

The user read-model SQL was assembled by concatenating `UserSelectSql` with a `WHERE` clause. On the target runtime this produced invalid SQL because the boundary between `FROM pos.users AS u` and `WHERE` was not guaranteed.

## Fix
Added an explicit newline between `UserSelectSql` and the dynamic `WHERE` clauses in:

- `ListUsersAsync`
- `GetUserAsync`

## Runtime affected
- `GET /api/v1/users`
- `POST /api/v1/users`
- `PATCH /api/v1/users/{userId}`

## Not changed
- No DB migrations
- No OpenAPI change
- No endpoint contract change
- No permission change
- No tenant/store logic change

## Validation required
Run:

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```

Then validate users endpoints.
