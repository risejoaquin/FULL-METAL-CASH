# SolidPOS — GA-12 Hotfix 12.1

## Scope

Fixes a GA-12 documentation contract guardrail failure.

## Root cause

The GA-12 validator required the literal phrase `does not activate` in `docs/ga/ga-12-final-general-availability-launch-readiness.md`, while the document used equivalent wording with markdown emphasis: `does **not** activate`.

## Fix

- Validator version updated to `GA-12.1-document-contract-activation-wording`.
- GA-12 readiness document now includes the exact required phrase `does not activate`.
- GA-12 command document updated to select the new validator marker.

## Impact

No backend, database migration, API contract, dashboard, PosCore, PosBuilder, schemaVersion, or syncContract changes.

## Decision

This is a documentation/validator contract hotfix only. General Availability public launch remains not activated.
