# SOLIDPOS LGA-04 HOTFIX 04.1 — Sync Contract Schema Source Alignment

## Objective
Align the LGA-04 validator with the production sync contract endpoint shape.

## Issue
The first LGA-04 run passed repository guardrails, LGA-03 prerequisite, build, tests, secret scan and API readiness, then failed during API checks because the validator required `schemaVersion` directly from `GET /api/v1/sync/contract`.

Production does not expose that property in the current endpoint response. The authoritative schema gate for LGA-04 is the database decision snapshot, which already asserts:

- `schemaVersion = 4`
- `syncContract = schema_version_4`

## Change
`validate-lga-04-public-ga-decision-readiness-capacity-remediation.ps1` now resolves the schema contract from any supported API response shape:

- `schemaVersion`
- `currentSchemaVersion`
- `syncContract = schema_version_4`
- `contract = schema_version_4`

If the endpoint does not expose one of those fields, the validator logs that the authoritative assertion is deferred to the DB snapshot and continues to the DB gate.

## Safety
This hotfix does not relax the final contract. LGA-04 still fails unless the DB snapshot confirms schema version 4 and sync contract `schema_version_4`.

## Status
Ready for rerun of LGA-04.
