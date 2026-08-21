# EXP-07 — GO / NO-GO

## GO

EXP-07 is GO when:

- health/live is alive
- health/ready is ready
- database is ready
- sync status endpoint returns data
- sync contract endpoint returns data
- dead-letter endpoint returns data
- conflicts endpoint returns data
- SQL blocking reasons are empty
- pending conflicts are zero
- stale processing events are zero
- duplicate batch/sequence violations are zero
- every warning has owner, threshold, action, and escalation

## NO-GO

NO-GO if any blocker is present: required table missing, tenant inactive, no processed sync evidence, pending conflicts, stale processing, duplicate idempotency violation, invalid sync status, or database not ready.

## Next phase

EXP-08 — Support and Incident Operations
