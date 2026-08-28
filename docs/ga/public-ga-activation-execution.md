# Public GA Activation Execution

This is the controlled execution step after the reviewed Public GA Activation Decision returned GO APPROVED.

The execution persists the authoritative rollout state in `pos.tenant_configs.feature_flags` for the target tenant. It sets `generalAvailabilityActivated=true`, `publicGeneralAvailabilityActivated=true`, `publicGaActivation=ACTIVATED`, and `rolloutStage=public_ga` in one database transaction.

This activation is an operational rollout-state transition. It does not change schema version 4, the `schema_version_4` sync contract, pricing, inventory semantics, RBAC, or sales behavior.

## Safety boundary

Activation requires all of the following simultaneously:

- explicit `-ExecuteActivation` switch;
- exact confirmation phrase `ACTIVATE_PUBLIC_GA`;
- successful Public GA Activation Decision preflight or reviewed prerequisite logs;
- health capacity at concurrency 3 / 6 requests with p95 <= 1200 ms;
- negative stock within baseline;
- waiting connections <= 12;
- zero long-running queries.

After the transaction, the validator rechecks the authoritative state and repeats the health/capacity and database safety checks. Any postflight failure triggers automatic rollback unless explicitly disabled.

## Success state

`PASS PUBLIC GA ACTIVATION EXECUTION / PUBLIC GA ACTIVATED / POSTFLIGHT PASS`

The next required block is `POST_PUBLIC_GA_ACTIVATION_VALIDATION_REQUIRED`.
