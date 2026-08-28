# SolidPOS GA-07 Hotfix 07.2 — Child Script Exit-Code Isolation

## Cause
GA-07 read the process-global `$LASTEXITCODE` immediately after invoking PowerShell child scripts. A successful `scan-local-secrets.ps1` does not necessarily overwrite that variable, so a stale non-zero exit code from an earlier native command could produce a false blocker even when the scan printed `No obvious secret patterns found.`

## Fix
- Reset `$global:LASTEXITCODE = 0` immediately before child PowerShell script invocation.
- Capture the value immediately after the child returns.
- Reset the global value again before continuing.
- Apply the same isolation to both the local secret scan and the nested GA-06 validator.
- Preserve GA-06's exact manifest/status validation as the authoritative prerequisite gate.

## Scope
Validator-only. No schema, API, server runtime, migration, commercial data, release, cohort, or rollout change.
