# HOTFIX GA-06.1 — Document Contract Backward Compatible

## Cause
GA-06 repository guardrails require the canonical phrase `backward compatible` in `docs/ga/ga-06-cohort-targeting-contract.md`. The implementation and behavior were already backward compatible, but the document described the behavior without that exact phrase.

## Change
The cohort targeting contract now states explicitly that the optional terminal targeting extension is backward compatible with existing tenant-wide releases.

## Scope
Documentation only. No API, database, migration, targeting, promotion, rollback, schema v4, sync contract, or production behavior changed.

## Status
PENDING USER VALIDATION.
