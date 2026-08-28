# HOTFIX GA-08.2 — Canonical GA-07 Manifest Name

## Root cause
GA-08 revalidated GA-07 successfully, but afterward looked for `ga-07-dr-manifest.json` while the canonical GA-07 validator writes `ga-07-manifest.json`.

## Fix
- `validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1` now consumes the canonical `.runtime/ga-07-backup-restore-rollback-disaster-recovery/ga-07-manifest.json`.
- No database, API, schema, production deployment, or security policy changes.
- Migration 020 does not need to be reapplied for this hotfix.

## Validation
Re-run only GA-08 after confirming `DATABASE_URL`, `$securePassword`, and the rotated-secret acknowledgement are present.

Expected gate:
`[GA-08] Fresh GA-07 prerequisite revalidation PASS`

Final expected closure:
`[GA-08] GA-08 PASS GA SECURITY TENANT ISOLATION ACCESS CONTROL / GO GA-09`
