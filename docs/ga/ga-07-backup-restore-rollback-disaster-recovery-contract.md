# GA-07 Backup Restore Rollback Disaster Recovery Contract

Entry gate: GA-06 PASS.

The GA-07 drill verifies a readable full logical PostgreSQL backup, isolated restore with production-data count reconciliation, explicit RPO and RTO targets, Velopack rollback artifact availability, transactional stable release revoke/restore with zero persisted rollback mutation, post-drill readiness, append-only rollback audit evidence, schema version 4, and sync contract schema_version_4.

The restore is isolated. No destructive production restore is permitted. General Availability remains not activated and public rollout remains disallowed.

Required result: PASS GA BACKUP RESTORE ROLLBACK DISASTER RECOVERY / GO GA-08.
