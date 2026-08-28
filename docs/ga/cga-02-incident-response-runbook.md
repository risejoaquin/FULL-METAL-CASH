# CGA-02 Incident Response Runbook

## P0

Examples:

- duplicate financial records
- tenant data leakage
- Public GA activated without explicit decision
- unrecoverable sync corruption
- API unavailable during controlled rollout

Action: pause rollout, preserve evidence, rollback if required, create hotfix, and do not continue to CGA-03.

## P1

Examples:

- repeated health/ready failures
- waiting connections rising materially
- sync retry/conflict counts above zero
- dashboard unavailable
- repeated upstream 400 during limited rollout

Action: keep rollout limited, reduce terminal/store scope if needed, diagnose Railway/DB/backend, and rerun CGA-02.

## P2

Examples:

- historical dead-letter event remains stable
- low stock warnings
- dashboard build skipped by explicit switch
- latency warning that does not break thresholds

Action: record condition and continue only if blockers remain empty.

## Rollback triggers

Rollback is required for financial duplication, data leakage, unrecoverable sync corruption, or Public GA activation without explicit authorization.

## Waiting connections

Waiting connections are a known condition. CGA-02 records them and carries them forward to CGA-03 for capacity / DB remediation or formal acceptance.
