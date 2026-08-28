# SolidPOS LGA-07 HOTFIX 07.1 — DB Pressure Diagnostics

## Changed

- Added DB pressure diagnostic validator.
- Added diagnostic SQL snapshot from `pg_stat_activity`.
- Added documentation and validation commands.

## Modules affected

- `scripts/ga`
- `docs/ga`
- validation commands

## Technical decision

The repeated value `waitingConnectionCount = 13` is not accepted as a new Limited GA baseline. LGA-07 remains pending until capacity is upgraded or DB/API pool behavior is remediated.

## Public GA

Public GA remains `NOT_ACTIVATED`.

## Risk

Connection pressure may be caused by constrained Railway resources, DB pool saturation, repeated probe runs, idle sessions, or slow health/readiness behavior. The hotfix collects enough evidence to decide between scaling and code-level connection pool remediation.
