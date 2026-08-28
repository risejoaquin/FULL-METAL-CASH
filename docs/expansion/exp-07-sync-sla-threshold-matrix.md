# EXP-07 — Sync SLA Threshold Matrix

| Signal | Owner | Threshold | Action | Escalation |
|---|---|---:|---|---|
| retry_pending_sync | Support L1 | > 0 for more than 15 minutes | review sync status and process queue | Support L2 |
| dead_letter_sync | Support L2 | > 0 | triage payload, classify reason, decide manual retry or incident | Engineering |
| pending_conflicts | Support L2 | > 0 | resolve conflict using documented strategy | Engineering |
| stale_processing | Engineering | > 0 older than 15 minutes | inspect worker/process logs | Incident Commander |
| duplicate_key_violation | Engineering | > 0 | stop expansion and verify idempotency guard | Incident Commander |
| sync_schema_version_mismatch | Engineering | > 0 new events | inspect terminal version and update channel | Release Owner |

Every alert must have an owner, threshold, action, and escalation path before GO.
