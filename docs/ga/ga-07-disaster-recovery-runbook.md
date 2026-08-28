# GA-07 Disaster Recovery Runbook

1. Confirm incident authority and freeze release mutations.
2. Confirm latest verified backup identity and SHA-256.
3. Confirm rollback version and signed Velopack artifact availability.
4. Restore backup into an isolated verification database first.
5. Reconcile critical commercial counts and integrity before any production recovery decision.
6. For application rollback, revoke the affected stable release only under explicit incident authority and promote/serve the validated rollback artifact through the controlled update channel.
7. Validate `/health/live`, `/health/ready`, sync queues, sales/payment reconciliation, inventory, and update checks.
8. Record an append-only audit event with recovery evidence.
9. Production restore is never executed by the GA-07 validator; real disaster recovery requires an explicit operator-authorized runbook execution.

RPO target for GA-07 drill: 300 seconds. RTO target for GA-07 drill: 900 seconds.
