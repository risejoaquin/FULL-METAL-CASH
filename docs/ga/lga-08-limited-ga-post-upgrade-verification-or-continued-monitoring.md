# LGA-08 — Limited GA Post-Upgrade Verification or Continued Monitoring

## Objective

LGA-08 begins only after LGA-07 PASS and verifies that SolidPOS remains stable in Limited GA. It supports two explicit modes without activating Public GA:

- **CONTINUED_MONITORING**: keep the current limited-capacity environment under the existing formal capacity risk acceptance.
- **POST_UPGRADE_VERIFICATION**: verify an infrastructure upgrade performed outside the validator; the capacity probe must pass before this mode can close.

## Entry Gate

- LGA-07 PASS is required.
- Limited GA remains the only authorized rollout state.
- Public GA not activated.
- Schema version 4 and `schema_version_4` remain mandatory.

## Non-negotiable invariants

- `PublicGaDecision = KEEP_LIMITED_GA` only.
- `publicGaActivation = NOT_ACTIVATED`.
- `generalAvailabilityActivated = false`.
- `publicGeneralAvailabilityActivated = false`.
- Negative stock baseline remains zero; any regression blocks LGA-08.
- Waiting connections baseline remains `<= 12`; this phase does not raise it.
- Existing sync conflict baseline may not exceed 3.
- Existing dead letter baseline may not exceed 1.
- No legacy schema events; schema version 4 remains authoritative.
- No Public GA action is performed by the validator.

## Verification surface

The validator checks local build/tests, secret scanning, WPF command-state guardrails, API health, authenticated observability, sync status/contract, inventory read API, sales/dashboard read models, dashboard URL, a concurrency capacity probe, and a PostgreSQL operational snapshot.

## Capacity decisions

### Continued monitoring

Use `-VerificationMode CONTINUED_MONITORING` and `-CapacityDecision FORMAL_ACCEPT_LIMITED_CAPACITY`. A failed capacity probe is recorded but is not a blocker when all Limited GA operational baselines remain within bounds.

### Post-upgrade verification

Use `-VerificationMode POST_UPGRADE_VERIFICATION` only after Railway/infrastructure capacity was changed externally. In this mode the live and ready concurrency probes must have zero failures and p95 values within the configured threshold. Failure blocks the phase and requires rollback/remediation or a return to continued monitoring.

## Exit criteria

LGA-08 closes only when the blocker matrix is empty and the validator prints one of these PASS outcomes:

- `PASS LGA-08 LIMITED GA POST-UPGRADE VERIFICATION OR CONTINUED MONITORING / CONTINUE LIMITED GA`
- `PASS LGA-08 POST-UPGRADE CAPACITY VERIFICATION / KEEP LIMITED GA - PUBLIC GA NOT ACTIVATED`

No later phase is authorized by this package. The manifest records `nextPhase = NOT_AUTHORIZED_UNTIL_LGA_08_PASS_REVIEW`.
