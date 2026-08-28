# Post-LGA Capacity / Infrastructure Remediation Package Manifest

## Base

Full repository package based on the reviewed LGA-12 PASS state.

## Runtime code changed

- `src/PosServer/SolidPOS.PosServer.Infrastructure/PostgreSql/PostgreSqlReadinessProbe.cs`
  - replaces 12 SQL commands per readiness request with one parameterized catalog query;
  - retains all 11 required-table validations;
  - does not alter API contracts, migrations, schema version, authentication, authorization or tenant/RLS semantics.

## Validation assets added

- `scripts/ga/validate-post-lga-capacity-infrastructure-remediation.ps1`
- `scripts/ga/post-lga-capacity-infrastructure-remediation-check.sql`
- `POST_LGA_CAPACITY_VALIDATION_COMMANDS.md`
- `SOLIDPOS_POST_LGA_CAPACITY_INFRASTRUCTURE_REMEDIATION.md`
- `POST_LGA_CAPACITY_PACKAGE_MANIFEST.md`
- `docs/ga/post-lga-capacity-infrastructure-remediation.md`
- `docs/ga/post-lga-capacity-remediation-plan.md`
- `docs/ga/post-lga-capacity-validation-checklist.md`
- `docs/ga/post-lga-capacity-evidence-matrix.md`

## Immutable guardrails

- `schemaVersion = 4`
- `syncContract = schema_version_4`
- `AllowedWaitingConnectionCount = 12`
- `AllowedNegativeStockItemCount = 0`
- `AllowedExistingSyncConflictCount = 3`
- `AllowedDeadLetterCount = 1`
- `PublicGaReadinessConcurrency = 3`
- `ConcurrencyProbeRequests = 6`
- `MaxReadinessP95Ms = 1200`
- `PublicGaDecision = KEEP_LIMITED_GA`
- `publicGaActivation = NOT_ACTIVATED`

## PASS semantics

The new validator is intentionally stricter than LGA-12. Formal acceptance of limited capacity is not a PASS path. A PASS requires both live and ready probes to complete with zero failures and p95 <= 1200 ms at concurrency 3.

A PASS authorizes only a Public GA readiness review. It does not activate Public GA.

## Environment limitation

This packaging environment does not provide the .NET SDK or PowerShell runtime, so production-equivalent `dotnet build/test` and PowerShell execution cannot be claimed here. Those checks are embedded in the validator and must run on the user's Windows validation machine before production PASS is accepted.
