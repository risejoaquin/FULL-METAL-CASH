# LGA-06 — Limited GA Operational Continuity and Capacity Upgrade Preparation

## Objective

Continue Limited GA after LGA-05 with **formal capacity risk accepted** while preparing the capacity upgrade path for Railway Pro or equivalent scaling.

## Entry Gate

LGA-05 must be closed as PASS:

- PASS LGA-05 CONTINUE LIMITED GA WITH FORMAL CAPACITY RISK ACCEPTED / CONTINUE LIMITED GA.
- capacityDecision = FORMAL_ACCEPT_LIMITED_CAPACITY.
- public ga decision = KEEP_LIMITED_GA.
- public ga not activated.

## Operational Continuity Scope

LGA-06 validates operational continuity under the current Limited GA scope:

- Maximum stores: 2.
- Maximum concurrent terminals: 2.
- Schema version 4.
- syncContract = schema_version_4.
- Conflict baseline carried forward.
- Dead letter baseline carried forward.
- Negative stock must remain zero.
- Open shifts must remain zero at checkpoint close.
- Capacity risk remains accepted only for Limited GA.

## Capacity Upgrade Preparation

Capacity upgrade preparation is required before Public GA. The current capacity remediation remains deferred until Railway Pro or an equivalent scaling upgrade is available.

The accepted temporary state is:

- capacityProbePassed may be false.
- capacityDecision = FORMAL_ACCEPT_LIMITED_CAPACITY.
- Public GA must remain off.
- Capacity remediation is tracked, not waived.
- Railway Pro / scaling upgrade will be verified in a later gate.

## Public GA Decision

The public ga decision remains KEEP_LIMITED_GA.

LGA-06 does not activate Public GA, does not change thresholds, and does not promote public rollout.

## Exit Criteria

LGA-06 may pass when:

- LGA-05 PASS is already available.
- Limited GA API checks pass.
- Database snapshot passes.
- Blocker matrix is empty.
- Public GA remains NOT_ACTIVATED.
- Capacity upgrade preparation is documented.
