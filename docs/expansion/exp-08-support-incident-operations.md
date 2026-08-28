# EXP-08 — Support and Incident Operations

## Objective

EXP-08 converts the production expansion state into a repeatable support and incident operations system.

Scope:
- support and incident operations
- dead-letter triage
- retry due handling
- incident evidence
- SEV classification
- escalation and handoff
- operational rollback
- support bitacora
- daily support triage
- GO EXP-09 when support operations are ready

## Non-destructive rule

EXP-08 is operational hardening. It does not change store, terminal, sale, payment, cash shift, inventory, or sync data.

## Inputs

- EXP-01 baseline PASS
- EXP-02 readiness pack PASS
- EXP-03 second terminal PASS
- EXP-04 second store PASS
- EXP-05 monitoring hardening PASS
- EXP-06 inventory reconciliation PASS
- EXP-07 sync SLA and offline reliability PASS

## Live conditions handled

- retry pending sync
- retry due events
- known dead-letter
- open cash shift daily review

## Output

Support and incident operations ready for release management.

Next phase: EXP-09 Release Management and Update Channel.
