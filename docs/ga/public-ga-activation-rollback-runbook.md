# Public GA Activation Rollback Runbook

Rollback returns the rollout-state markers to Limited GA / NOT_ACTIVATED without changing schema version 4 or business data.

Automatic rollback is enabled by default in the activation validator when a postflight check fails after the activation transaction.

Manual rollback requires the exact phrase `ROLLBACK_PUBLIC_GA` and a reason. The rollback writes `generalAvailabilityActivated=false`, `publicGeneralAvailabilityActivated=false`, `publicGaActivation=NOT_ACTIVATED`, `rolloutStage=limited_ga`, and rollback timestamp/reason metadata.

Rollback does not delete sales, payments, receipts, inventory ledger entries, audit events, or sync history.
