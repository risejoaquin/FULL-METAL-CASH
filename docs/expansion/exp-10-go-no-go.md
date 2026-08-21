# EXP-10 GO/NO-GO

## GO
EXP-10 is GO when:
- dotnet restore/build/test pass.
- production liveness/readiness pass.
- tenant current endpoint passes.
- stores/users/roles/permissions/terminals endpoints pass.
- customer create/list/get/update/sales endpoints pass.
- controlled user create/update/list endpoint flow passes.
- SQL cross-check passes.
- audit evidence for user/customer mutations exists.
- blockers are empty.

## NO-GO
EXP-10 is NO-GO when:
- build or tests fail.
- health/readiness fails.
- RBAC endpoint contract fails.
- customer or user mutation fails.
- audit evidence is missing.
- SQL cross-check reports blockers.

## Next phase
GO authorizes EXP-11 — Catalog Pricing Operations.
