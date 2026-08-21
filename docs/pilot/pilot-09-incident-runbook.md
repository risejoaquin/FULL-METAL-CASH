# SolidPOS PILOT-09 Pilot Incident Runbook

## Purpose

Validate that pilot operators can detect, classify, contain, recover, verify, and close incidents using documented steps instead of improvising during production pilot operations.

## Scope

This runbook covers health degradation, readiness failure, database unavailable, auth incident, terminal enrollment incident, offline terminal incident, sync backlog, retry_pending growth, dead_letter incident, sync conflict incident, inventory inconsistency, cash drawer incident, receipt generation incident, backup, restore, rollback, audit trail, escalation, and GO/NO-GO decisions.

## Severity model

### SEV1

Customer operation is blocked, cash cannot be reconciled, database is unavailable, readiness failure persists, auth incident blocks all users, or backup/restore/rollback is required.

Immediate action: stop risky changes, preserve evidence, notify owner/operator, keep POS offline if safe, and execute rollback decision tree when data integrity is at risk.

### SEV2

Operation continues with degraded sync, terminal enrollment issue, offline terminal issue, dead_letter growth, sync conflict, inventory inconsistency, or repeated API failures.

Immediate action: contain the affected terminal/store workflow, keep sales flowing when safe, triage the queue/conflict, and reconcile before daily close.

### SEV3

Non-blocking receipt generation issue, reporting delay, isolated retry, or dashboard warning that does not block selling.

Immediate action: document evidence, retry safe operations, monitor trend, and close after verification.

## Incident matrix

| Incident | Detector | Severity | Containment | Recovery | Verification |
| --- | --- | --- | --- | --- | --- |
| Health degradation | `/health/live`, dashboard API monitor | SEV2 | Avoid deploys and collect logs | Restart/redeploy only after evidence | Health returns alive |
| Readiness failure | `/health/ready` | SEV1 | Stop deploys and avoid migrations | Check DB, secrets, Railway, Supabase | Readiness returns ready |
| Database unavailable | `/health/ready` database status | SEV1 | Switch terminal to offline mode when safe | Restore DB connectivity, evaluate backup/restore | API and SQL checks pass |
| JWT/auth incident | `/api/v1/auth/login` | SEV1 | Stop user changes, verify tenant status | Rotate keys or fix auth config | Login returns accessToken |
| Terminal enrollment incident | terminal register endpoint | SEV2 | Do not reuse suspicious terminal token | Verify fingerprint and terminal status | Terminal binds locally |
| Offline terminal incident | PosCore local integrity | SEV2 | Keep selling within 72 hours if authorized | Reconnect, push sync, pull sync | Outbox clean and read models updated |
| Sync backlog | `/api/v1/sync/status` | SEV2 | Prevent duplicate manual operations | Process sync and inspect retry queue | backlog stable/decreasing |
| retry_pending growth | sync metrics inboxByStatus | SEV2 | Identify repeated event type | Retry or fix payload | retry_pending not growing |
| dead_letter incident | `/api/v1/sync/dead-letter` | SEV2 | Quarantine bad payload | Manual retry only after root cause | dead_letter explained or cleared |
| Sync conflict incident | `/api/v1/sync/conflicts` | SEV2 | Preserve both versions | Resolve use_server or use_client by evidence | conflict status resolved |
| Inventory inconsistency | dashboard inventory risk | SEV2 | Stop blind stock edits | Reconcile ledger and physical count | negative inventory explained |
| Cash drawer incident | cash shift summary | SEV1 | Freeze close and preserve drawer evidence | Reconcile movements and cash count | differenceCents explained |
| Receipt generation incident | receipt endpoints | SEV3 | Keep sale record and issue digital receipt | Retry receipt generation/email | receipt public/protected works |
| Backup restore incident | PILOT-08 backup manifest | SEV1 | Do not mutate production further | Restore isolated first; use rollback tree | restore validation GO |

## Rollback decision tree

1. If production health is down and data integrity is unknown, classify SEV1.
2. If a migration or deploy caused the incident, freeze changes and collect commit/deploy identifiers.
3. If data was not persisted, prefer transactional rollback or redeploy.
4. If data was persisted incorrectly, use backup/restore evidence and write a scoped correction plan.
5. Never restore over production without explicit operator approval and backup manifest verification.

## Escalation path

1. Operator captures timestamp, tenantId, storeId, terminalId, user email, endpoint, and error text.
2. Engineer reviews dashboard metrics, health endpoints, sync status, dead-letter, conflicts, and audit trail.
3. Owner approves SEV1 rollback, restore, or production configuration change.
4. Close only after GO/NO-GO evidence is written.

## Audit trail requirements

Each incident must record severity, detector, containment action, recovery action, verification result, and GO/NO-GO decision. Use audit events when available and preserve PowerShell output/log artifacts.

## GO/NO-GO

GO requires health ready, database ready, auth working, runbook document available, metrics available, SQL cross-check passing, and all observed incidents classified with an action path.

NO-GO if readiness fails, database unavailable, auth fails, runbook missing severity/action, rollback leaves persisted probe rows, or incident state cannot be explained.
