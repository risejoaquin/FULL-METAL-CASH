# EXP-02 Store Rollback Runbook

Trigger: readiness failure, database unavailable, pending conflicts, failed payments, terminal enrollment failure or store isolation failure.

Containment: stop affected store, preserve first store if unaffected, keep evidence.

Decision: NO-GO until health/readiness, sync, payments, evidence and recovery are stable.
