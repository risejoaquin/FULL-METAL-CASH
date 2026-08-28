# LGA-07 — Limited GA Capacity Upgrade Execution or Continued Monitoring

## Objective

LGA-07 continues Limited GA after LGA-06 operational continuity and capacity upgrade preparation. This phase supports two controlled outcomes:

1. **capacity upgrade execution** if Railway Pro / scaling has already been applied outside the validator.
2. **continued monitoring** if the system remains on the current limited-capacity environment.

## Entry Gate

Required entry gate:

- LGA-06 PASS.
- LGA-06 operational continuity confirmed.
- formal capacity risk accepted remains valid.
- public ga not activated.

## Public GA Control

Public GA must remain disabled during LGA-07.

- public ga decision: KEEP_LIMITED_GA.
- public ga not activated.
- publicGaActivation: NOT_ACTIVATED.

## Capacity Policy

LGA-07 does not change production infrastructure by itself. Capacity remediation remains either:

- performed externally through Railway Pro / scaling and then verified by this validator, or
- deferred under continued monitoring while Limited GA remains active.

capacity upgrade execution is considered verified only if the health concurrency probes pass the configured threshold.

continued monitoring remains valid only if blockers remain empty and the accepted limited-capacity baseline is not exceeded.

## Scope

- Limited GA only.
- Maximum active stores: 2.
- Maximum concurrent terminals: 2.
- Capacity risk remains accepted until Railway Pro / scaling is completed.
- schemaVersion 4 and syncContract schema_version_4 remain mandatory.

## Exit Criteria

LGA-07 exits as PASS when:

- LGA-06 prerequisite is satisfied.
- Repository/document guardrails pass.
- Build/tests/secret scan pass.
- Operational API checks pass.
- Database snapshot passes.
- Blocker matrix is empty.
- Public GA remains NOT_ACTIVATED.

## Next Phase

- If capacityProbePassed is false: LGA-08 - Limited GA Post-Upgrade Verification or Continued Monitoring.
- If capacityProbePassed is true: LGA-08 - Post-Upgrade Capacity Verification and Public GA Readiness Decision.
