# CGA-03 — Capacity / DB Remediation or Formal Acceptance

## Purpose

CGA-03 converts the known capacity and database pressure findings into an explicit controlled-GA decision.

This phase does **not activate Public GA**. It either validates remediation or records a formal acceptance of the limited rollout envelope.

## Accepted entry gate

- CGA-02 PASS REAL PRODUCTION / GO CGA-03.
- Controlled rollout remains LIMITED.
- Public GA remains NOT ACTIVATED.

## Decision modes

### FORMAL_ACCEPTANCE

Accepts the current limited operating envelope:

- maximum 2 active stores for rollout scope;
- maximum 2 concurrent/open terminals/shifts;
- known sync conflict baseline may be carried if explicitly allowed;
- known dead letter baseline may be carried if explicitly allowed;
- waiting DB connections may be carried if explicitly allowed;
- Public GA is not activated.

### REMEDIATION_VALIDATION

Requires the known baselines to be zero or remediated:

- zero sync conflict allowance;
- zero dead letter allowance;
- zero waiting connection allowance;
- Public GA still remains not activated until CGA-04.

## Known carried conditions

- GA-09 capacity boundary: concurrency 3+ on current Railway/upstream path can return 400 upstream error.
- DB waiting connections observed through GA-10/GA-11/GA-12/Post-GA-12/CGA-01/CGA-02.
- Known CGA-02 sync conflict baseline from the first PosCore terminal run.
- Known dead letter baseline.

## Output

The validator writes:

`.runtime/cga-03-capacity-db-remediation-or-formal-acceptance/cga-03-capacity-db-remediation-or-formal-acceptance-manifest.json`

The successful status is either:

- `PASS CGA-03 FORMAL LIMITED CAPACITY ACCEPTANCE / GO CGA-04`
- `PASS CGA-03 CAPACITY DB REMEDIATION VALIDATION / GO CGA-04`
