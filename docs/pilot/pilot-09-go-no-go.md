# PILOT-09 GO/NO-GO

## GO

PILOT-09 is GO when:

- health/live returns alive.
- health/ready returns ready.
- database readiness is ready.
- admin auth returns accessToken.
- observability metrics are available.
- sync status, dead-letter, conflicts, and audit endpoints respond.
- SQL scoped validation returns GO.
- incident runbook includes severity, escalation, rollback, audit, and GO/NO-GO rules.
- SEV1 and SEV2 paths have containment and recovery instructions.

## NO-GO

PILOT-09 is NO-GO when:

- readiness failure persists.
- database unavailable state is detected.
- auth incident blocks admin login.
- incident runbook omits SEV1 or SEV2 instructions.
- rollback or restore decision tree is missing.
- audit trail requirements are missing.
- SQL validation cannot confirm tenant and required operational tables.

## Required severity handling

- SEV1: freeze deploys, preserve evidence, escalate to owner, evaluate rollback.
- SEV2: contain affected workflow, keep safe operations running, reconcile before close.
- SEV3: monitor, document, retry safe operations, close after verification.

## Rollback rule

Rollback requires explicit operator approval and must reference backup or restore evidence. No direct destructive production restore is allowed during this validation.
