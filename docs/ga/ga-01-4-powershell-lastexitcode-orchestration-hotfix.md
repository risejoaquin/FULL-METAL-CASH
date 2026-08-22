# HOTFIX GA-01.4 — PowerShell LASTEXITCODE orchestration semantics

## Failure observed

GA-01 printed `No obvious secret patterns found` but immediately failed with `Secret scan failed with exit code 3`.

## Root cause

`$LASTEXITCODE` is the exit code of the last native executable. A successful PowerShell script invocation does not guarantee that this value is reset. GA-01 was therefore treating a stale native-process exit code as the result of `scan-local-secrets.ps1`.

## Correction

- PowerShell child validators are no longer judged using `$LASTEXITCODE`.
- Their real failures continue to propagate through terminating errors because validators use `throw` / `$ErrorActionPreference = Stop`.
- GA-01 continues to validate the fresh BETA-10 manifest and its formal gate after execution.
- `$LASTEXITCODE` remains in use for native commands such as Docker/psql, where it is semantically valid.
- The value is cleared around PowerShell child-script orchestration so stale native codes cannot leak into later steps.

## Scope

No database, schema, sync, inventory, tenant, release, or business-contract changes.

## State

`PENDING USER VALIDATION`
