# HOTFIX GA-01.1 — Windows PowerShell 5.1 Compatibility

## Trigger

GA-01 failed during `Repository/document GA baseline guardrails` with:

```text
[System.IO.Path] does not contain a method named GetRelativePath
```

The repository build and test suites had already passed. The failure is validator runtime compatibility with Windows PowerShell 5.1 / .NET Framework, not a production-data or GA-readiness blocker.

## Root cause

`[System.IO.Path]::GetRelativePath()` is available in modern .NET but not in the .NET Framework runtime used by Windows PowerShell 5.1. Two repository scripts referenced it:

- `scripts/ga/validate-ga-01-general-availability-baseline-freeze.ps1`
- `scripts/security/scan-local-secrets.ps1`

## Remediation

Both scripts now use `Get-RelativePathCompat`, implemented only with APIs available to Windows PowerShell 5.1. The helper:

1. normalizes base and target with `[IO.Path]::GetFullPath`;
2. applies case-insensitive Windows path comparison;
3. returns the target path relative to the repository root;
4. falls back to the absolute target only if it is outside the base path.

No database, schema, sync, inventory, release, tenant, or application contract changed.

## Family audit

The complete repository was searched for `GetRelativePath`. Both occurrences were corrected.

## Architecture invariants

- `schemaVersion = 4`
- `syncContract = schema_version_4`
- `inventory_ledger` remains inventory source of truth
- modifier semantics remain `none | add | substitute`
- General Availability remains NOT activated

## Validation status

`PENDING USER VALIDATION` until the GA-01 validator is rerun and returns:

```text
PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02
```
