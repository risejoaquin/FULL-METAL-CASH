# EXP-08 — GO / NO-GO

## GO

GO when:

- support documentation exists
- SEV classification exists
- support evidence template exists
- daily triage checklist exists
- escalation runbook exists
- rollback runbook exists
- support bitacora exists
- health and readiness are ready
- audit evidence is present
- pending conflicts are zero
- stale processing events are zero
- SQL blockers are zero

## NO-GO

NO-GO when:

- required support document is missing
- health or readiness fails
- audit evidence is missing
- pending conflicts exist
- stale processing exists
- tenant/store/terminal context is invalid
- support cannot classify or escalate incidents

Next phase after GO: EXP-09 Release Management and Update Channel.
