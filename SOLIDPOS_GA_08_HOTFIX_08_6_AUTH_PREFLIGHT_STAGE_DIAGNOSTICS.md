# SolidPOS GA-08 Hotfix 08.6 — Auth Preflight and Stage Diagnostics

## Diagnosis
The latest production log still failed at the opaque PowerShell helper line instead of the GA-08.5 stage-aware catch. That means the active local validator was not the expected diagnostic version or the password variable was not freshly loaded before execution.

## Fix
This hotfix adds an explicit validator version banner, validates that the SecureString resolves to a non-empty password before the long GA prerequisite chain, and wraps API failures with method/path/status/response context.

## Scope
Only the GA-08 validator changed. No PosServer runtime, PostgreSQL schema, RLS, JWT implementation, CORS, dashboard, schemaVersion, or sync contract changes.
