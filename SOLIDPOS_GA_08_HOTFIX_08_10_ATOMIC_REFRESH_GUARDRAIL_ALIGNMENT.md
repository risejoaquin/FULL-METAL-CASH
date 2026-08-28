# SolidPOS GA-08 Hotfix 08.10 — Atomic Refresh Guardrail Alignment

## Scope

This hotfix corrects the GA-08 validator guardrail after GA-08.9 moved refresh-token rotation from a two-step service-level contract to an atomic repository-level operation under tenant RLS.

## Root cause

The production/backend fix introduced in GA-08.9 was present, but the GA-08 validator still searched for the obsolete service-level marker `RotateRefreshTokenAsync` plus the old diagnostic string `Refresh token reuse or race rejected` inside `AuthService.cs`.

Because GA-08.9 now calls `RotateRefreshTokenAndFindUserAsync(...)`, the validator failed during the repository/source guardrail before reaching production authentication validation.

## Change

The validator now checks the actual GA-08.9 contract:

- `AuthService.cs` calls `RotateRefreshTokenAndFindUserAsync` and requires `request.TenantId`.
- `PostgreSqlAuthRepository.cs` contains the atomic tenant-scoped refresh rotation method.
- The repository guardrail verifies tenant session establishment before refresh-token access under RLS.

## No backend behavior change

This hotfix does not change runtime backend behavior. It only aligns the validator with the GA-08.9 backend design.

## Must preserve

- `schemaVersion = 4`
- `syncContract = schema_version_4`
- tenant-scoped refresh/logout contract
- RLS isolation
- refresh rotation and reuse rejection
- General Availability not activated automatically
