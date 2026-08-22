# SolidPOS — HOTFIX GA-06.10

## Transactional DB JSON Output Parsing

### Status
PENDING USER VALIDATION

### Root cause
GA-06 successfully promoted the identical RC through `internal -> beta -> stable` and passed the targeted update-check matrix. The rollback SQL then produced a valid JSON evidence row followed by PostgreSQL's transaction command tag `ROLLBACK`. The validator incorrectly assumed the final non-empty stdout line was JSON.

### Architectural decision
The database helper must treat process success separately from payload extraction. Native `psql` exit code remains authoritative for command success; JSON evidence is then identified by actual JSON parsing rather than stdout position.

### Changes
- Hardened only `scripts/ga/validate-ga-06-stable-channel-promotion-cohort-update-dry-run.ps1`.
- `Invoke-DbJsonFile` scans output in reverse.
- Ignores `BEGIN`, `COMMIT`, `ROLLBACK`, and `SET` control tags.
- Returns the first line that parses successfully as JSON.
- Fails closed if no JSON exists.

### Audit of similar GA-06 risk
The remaining GA-06 flow was reviewed for the same positional-output assumption. The final SQL gate uses the same helper and is therefore protected by this fix. `Invoke-DbScalar` does not parse JSON and is not affected by transaction command tags in the current GA-06 calls.

### No runtime mutation
No API, database schema, release identity, cohort targeting, stable release, or rollback semantics are changed by this hotfix.

- schemaVersion: 4
- syncContract: schema_version_4
- generalAvailabilityActivated: False
