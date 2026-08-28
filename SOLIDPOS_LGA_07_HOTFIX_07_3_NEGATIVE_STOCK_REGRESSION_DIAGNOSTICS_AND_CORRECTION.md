# SolidPOS LGA-07 HOTFIX 07.3

## Summary

Adds a controlled diagnostic and optional correction gate for the LGA-07 negative stock regression blocker.

## Changed files

- `scripts/ga/validate-lga-07-hotfix-07-3-negative-stock-regression-diagnostics-and-correction.ps1`
- `scripts/ga/lga-07-hotfix-07-3-negative-stock-regression-diagnostics-and-correction-check.sql`
- `docs/ga/lga-07-hotfix-07-3-negative-stock-regression-diagnostics-and-correction.md`
- `HOTFIX_LGA_07_3_VALIDATION_COMMANDS.md`

## Non-goals

- Does not activate Public GA.
- Does not change LGA-07 thresholds.
- Does not accept negative stock as baseline.
- Does not mutate DB unless `-ApplyInventoryCorrection` is provided explicitly.
