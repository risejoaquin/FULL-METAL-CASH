# BETA-01 Support Contact Matrix

## Purpose
Define the support path used during controlled commercial beta. Real personal contact data is intentionally not stored in the repository; operators must fill deployment-specific contacts outside source control.

| Severity | Trigger | Primary role | Escalation | Evidence required |
|---|---|---|---|---|
| SEV-1 | Sales unavailable, tenant isolation failure, data corruption, payment integrity failure | On-call engineering | Product/operations owner | timestamp, tenant/store/terminal IDs, request correlation, sanitized logs, rollback decision |
| SEV-2 | Degraded checkout, sync dead-letter growth, cash/reconciliation blocker | Support lead | Engineering | affected flow, counts, audit events, sanitized logs, workaround |
| SEV-3 | Limited UI/runtime visibility issue, non-blocking retry, operator question | Support | Engineering backlog | reproduction, screenshots/logs without secrets, expected vs actual |

## SLA readiness contract
SEV classification, incident intake, retry/dead-letter triage, rollback decision path, customer communication ownership and evidence collection must follow the existing EXP-08/EXP-12 support runbooks. Contact addresses and phone numbers must not be committed to this file.
