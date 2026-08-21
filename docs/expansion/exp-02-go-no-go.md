# EXP-02 GO/NO-GO

GO criteria: dotnet restore, dotnet build, dotnet test, secret scan, health/readiness, admin login, protected metrics, SQL cross-check, document contract and rollback path PASS.

Condition: monitor_retry_pending_sync, triage_known_dead_letter, inventory_reconciliation_required.

Blocker / NO-GO: readiness not ready, database not ready, pending conflicts > 0, failed payments last 24 hours > 0, missing store/terminal baseline, missing rollback owner.

Next: GO EXP-03 only after EXP-02 PASS.
