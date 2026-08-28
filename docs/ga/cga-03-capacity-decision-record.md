# CGA-03 Capacity Decision Record

## Decision

Default decision for the current controlled rollout is `FORMAL_ACCEPTANCE`.

## Formal acceptance scope

- Rollout mode: LIMITED.
- Max stores: 2.
- Max concurrent terminals/open shifts: 2.
- Public GA: NOT ACTIVATED.
- General Availability activation: NOT ACTIVATED.

## Accepted known baselines

- Allowed existing sync conflicts: 3.
- Allowed dead letters: 1.
- Allowed waiting DB connections: 11.

These baselines are accepted only for controlled rollout. They do not authorize wider Public GA.

## Remediation validation path

Use `-Decision REMEDIATION_VALIDATION` only after capacity and DB issues are remediated. In this path, known baselines must be set to zero.

## Required next decision

CGA-04 must separately decide whether Public GA remains disabled, is delayed, or is activated. CGA-03 never activates Public GA.
