# SolidPOS Post-Pilot Baseline Release Notes

Release name: Post-Pilot Baseline Freeze
Recommended tag: v0.10.0-post-pilot.20260820
Status: PENDING USER VALIDATION

## Summary

This release freezes the first controlled production pilot baseline after PILOT-01 through PILOT-10 passed in production.

## Production decision

```text
GO_LIMITED_EXPANSION
```

## Included validation areas

- Real POS transaction.
- Cash drawer and shifts.
- Receipts, returns and refunds.
- Offline mode.
- Sync recovery and conflicts.
- Dashboard monitoring.
- Backup, restore and rollback drill.
- Incident runbook.
- Pilot closure and production expansion decision.

## Known monitored conditions

- retry_pending sync item.
- known dead_letter item.
- negative inventory item.

These are not blockers for limited expansion, but they must remain visible and addressed by EXP-05, EXP-06 and EXP-07.
