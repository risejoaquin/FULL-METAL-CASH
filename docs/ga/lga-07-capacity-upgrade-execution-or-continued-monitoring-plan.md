# LGA-07 Capacity Upgrade Execution or Continued Monitoring Plan

## Purpose

This plan documents capacity upgrade execution or continued monitoring for Limited GA.

## Path A — Capacity Upgrade Execution

Use this path only after Railway Pro / scaling has been applied outside the validator.

Validation expectations:

- /health/live concurrency probe succeeds.
- /health/ready concurrency probe succeeds.
- p95 is within MaxReadinessP95Ms.
- waiting connections stay within allowed baseline.
- no long-running queries.
- no Public GA activation.

## Path B — Continued Monitoring

Use this path while Railway Pro / scaling is not yet applied.

Validation expectations:

- capacity remediation is still deferred.
- formal capacity risk accepted remains active.
- Limited GA remains restricted to the accepted scope.
- support and operational monitoring remain active.
- Public GA remains NOT_ACTIVATED.

## Railway Pro / Scaling

Railway Pro or equivalent scaling is the intended capacity remediation path. After upgrade, rerun this validator with the same thresholds. Do not relax MaxReadinessP95Ms to create a pass.

## Rollback

If an upgrade is attempted and produces worse DB pressure, failed health checks, higher waiting connections, or long-running queries:

- keep Public GA disabled.
- maintain Limited GA scope.
- rollback the capacity change if needed.
- rerun LGA-07 after stabilization.
