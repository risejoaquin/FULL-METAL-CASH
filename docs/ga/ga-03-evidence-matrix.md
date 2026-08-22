# GA-03 Evidence Matrix

| Requirement | Evidence |
|---|---|
| GA-02 prerequisite | fresh `.runtime/ga-02-sync-queue-sla-closure/ga-02-manifest.json` |
| SEV1–SEV4 | `ga-03-severity-escalation-oncall-matrix.md` |
| incident intake / evidence / PIR | `ga-03-incident-intake-evidence-and-pir-template.md` |
| escalation / on-call / rollback authority | severity matrix + domain routing runbook |
| sync/cash/payment/release routing | `ga-03-domain-incident-routing-runbook.md` |
| explicit SLO/SLI/error budget | `ga-03-slo-sli-error-budget-contract.md` |
| daily support | `ga-03-daily-support-operating-checklist.md` |
| current production signals | GA-03 SQL snapshot + protected health/metrics/sync endpoints |
| audit evidence | PostgreSQL `pos.audit_events` and GA-02 closure actions |
| runtime manifest | `.runtime/ga-03-support-incident-slo-operations-readiness/ga-03-manifest.json` |
