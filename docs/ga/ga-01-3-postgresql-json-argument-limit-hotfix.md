# HOTFIX GA-01.3 — PostgreSQL JSON argument-limit compatibility

## Failure observed

GA-01 reached the production baseline snapshot after fresh BETA-10 revalidation, then PostgreSQL rejected the snapshot query with:

`ERROR: cannot pass more than 100 arguments to a function`

The failing statement used a single `json_build_object(...)` containing all GA-01 baseline fields.

## Root cause

PostgreSQL functions accept at most 100 arguments. Each JSON key/value pair consumes two function arguments, so the flat GA-01 snapshot crossed that hard server limit.

## Change

The output is still one flat JSON object with the same field names and value types. The query now builds two smaller `jsonb_build_object(...)` fragments and concatenates them with the JSONB `||` operator before converting the result to text.

- fragment 1: 48 function arguments
- fragment 2: 54 function arguments
- maximum per call: 54 < 100

No validator field mapping changed.

## Scope

Changed:

- `scripts/ga/ga-01-general-availability-baseline-freeze-check.sql`

Not changed:

- database schema
- migrations
- production data
- tenant/RLS contract
- `schemaVersion = 4`
- `syncContract = schema_version_4`
- inventory ledger semantics
- GA-01 blockers/conditions semantics
- GA activation state

## Validation status

`PENDING USER VALIDATION`

Expected terminal gate:

`PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02`
