# EXP-10 RBAC and Store Access Policy

## RBAC baseline
Admin operations must be protected by permissions, not by hidden SQL access.

Required permissions validated by this phase:
- tenant manage
- stores manage
- users manage
- roles manage
- terminals manage
- customers read
- customers manage

## Store access
Operational users should receive explicit store access through `storeIds`. Access must be tenant scoped and must not create cross-tenant visibility.

## Role assignment
Role assignment must use known role codes or role ids returned by `/api/v1/roles`. Unknown role codes or role ids are rejected.

## NO-GO
- User created without role assignment.
- User created without store assignment when stores exist.
- Admin mutation missing audit trail.
- Protected endpoint available without bearer token.
