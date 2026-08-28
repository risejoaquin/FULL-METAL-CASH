# Post-GA-12 Controlled Rollout Plan

The controlled rollout path is a limited launch plan after GA-12 readiness. It is not public General Availability.

## Scope

- Keep rollout limited to explicitly approved tenants/operators.
- Keep rollback instructions available before expansion.
- Keep capacity boundary visible during the rollout.
- Keep waiting connections visible during the rollout.

## Required controls

```text
rollback
capacity boundary
waiting connections
publicGeneralAvailabilityActivated=False
```

## Operational rule

Controlled rollout may proceed only while blockers remain empty and monitoring remains active.
