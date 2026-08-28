# LGA-07 HOTFIX 07.1 — DB Pressure Diagnostics

## Purpose

LGA-07.1 adds DB pressure diagnostics for the repeated LGA-07 blocker:

- `waiting_connections_exceed_allowed_baseline`
- `actual = 13`
- `allowed = 12`

This is diagnostic only. It does not activate Public GA. It does not increase the accepted waiting connections baseline. It does not change data, schema, API contracts, sync contracts, inventory, sales, payments, receipts, roles, permissions, or rollout flags.

## Decision

Public GA not activated.

No baseline increase.

Limited GA continues only if the existing accepted risk remains controlled.

If waiting connections remain at 13 or above, LGA-07 remains pending until Railway Pro/scaling or DB/API connection pool remediation is executed.

## Evidence terms

- lga-07.1
- db pressure
- waiting connections
- diagnostic only
- public ga not activated
- no baseline increase
- capacity upgrade
- db pool remediation
