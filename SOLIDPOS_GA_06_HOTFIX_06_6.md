# SolidPOS — HOTFIX GA-06.6

## Existing release idempotent cohort reconciliation

### Failure family
GA-06 created an `internal` release during an earlier run before cohort-target persistence was available in the deployed backend. After the backend was upgraded, the validator detected the existing release and skipped the POST, so the historical GA-06 validation release remained without its controlled cohort target.

### Technical correction
`PostgreSqlBuilderUpdatesRepository.CreateReleaseAsync` is now idempotent for an existing `(tenant, channel, packageType, version)` release:

1. It attempts the insert as before.
2. On conflict it reads the existing release without mutating it.
3. It requires exact identity equality for tenant scope, version, channel, package type, artifact URL, artifact hash, signature, rollback version, mandatory flag, universal-installer flag and non-revoked state.
4. If any identity field differs, the transaction rolls back and the request is rejected.
5. Only when identity is exact may missing `update_release_targets` rows be inserted with `ON CONFLICT DO NOTHING`.

The GA-06 validator now POSTs the exact desired contract even when a release row already exists, verifies the returned id is unchanged, then checks target persistence from PostgreSQL.

### Safety
- No update of an existing release row.
- No delete of release history.
- No artifact/hash/signature substitution.
- No public rollout activation.
- No mandatory update.
- Target reconciliation is append-only and limited to the validated controlled terminal.
- `schemaVersion = 4` and `syncContract = schema_version_4` remain unchanged.

### Deployment
This hotfix changes PosServer backend code and therefore must be deployed before rerunning GA-06.
