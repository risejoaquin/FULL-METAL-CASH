# Macro Phase 34 Hotfix 34.1 — Migration Smoke Test Channel Source Fix

## Status
Implemented — pending GitHub Actions validation.

## Root cause
The CI migration smoke test expected a physical table named `pos.update_channels`.

However, the runtime design implemented update channels (`stable`, `beta`, `internal`) as an application-level static catalog returned by `GET /api/v1/updates/channels`, while the database persists only release metadata in `pos.update_releases`.

This caused GitHub Actions job `migration-smoke-test` to fail after migrations and seed succeeded with:

```text
Missing required runtime tables: {update_channels}
```

## Fix
Updated:

```text
scripts/ci/migration-smoke-test.sh
```

Changes:

- Removed `update_channels` from required physical runtime tables.
- Kept `update_releases` as required DB runtime table.
- Removed SQL query against non-existent `update_channels`.
- Left channel behavior validated through application contract/tests and `GET /api/v1/updates/channels`.

## Not changed

```text
No endpoint change
No OpenAPI change
No production runtime code change
No database migration change
No Dockerfile change
No Railway config change
```

## Expected result
GitHub Actions should now pass:

```text
restore-build-test-contracts  PASS
environment-validation        PASS
docker-build                  PASS
migration-smoke-test          PASS
```
