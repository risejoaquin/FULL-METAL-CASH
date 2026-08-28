# SolidPOS — LGA-07 HOTFIX 07.2 DB Pressure Diagnostics Schema Compatibility

## Summary

HOTFIX LGA-07.2 fixes the diagnostic SQL used by HOTFIX LGA-07.1. The prior diagnostic attempted to reference `query_start` from the aggregation stage. The corrected diagnostic uses the `age` value already calculated in the activity CTE.

## Technical decision

No production behavior is changed. This is diagnostic only.

## Non-goals

- No public ga activation.
- No baseline increase.
- No DB mutation.
- No API changes.
- No inventory/sync/sales changes.

## Expected validation

`PASS HOTFIX LGA-07.2 DB PRESSURE DIAGNOSTICS SCHEMA COMPATIBILITY / LGA-07 REMAINS PENDING WHEN WAITING CONNECTIONS EXCEED BASELINE`
