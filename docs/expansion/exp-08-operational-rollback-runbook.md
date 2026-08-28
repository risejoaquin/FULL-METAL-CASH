# EXP-08 — Operational Rollback Runbook

Rollback is allowed only as containment for an active incident.

Rules:

- rollback must be documented
- containment must happen before destructive action
- no destructive delete is allowed for audit, sync, inventory ledger, payments, sales, or receipts
- append-only evidence is required when correcting operational state
- no secrets are copied into evidence

Allowed rollback categories:

- disable a new terminal operationally
- suspend expansion activity
- stop onboarding a new store
- revert deployment through approved release rollback
- use append-only ledger or support evidence where relevant

Forbidden:

- deleting production evidence
- rewriting audit history
- manually changing payment state without approved runbook
- bypassing tenant isolation
