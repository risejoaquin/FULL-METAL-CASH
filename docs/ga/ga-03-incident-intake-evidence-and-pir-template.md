# GA-03 — Incident Intake, Evidence and Post-Incident Review Template

## Incident intake

Every incident must record:

- incident ID;
- UTC opened timestamp;
- tenant ID and store/terminal IDs when applicable;
- detector / SLI / endpoint / SQL source;
- SEV1–SEV4 classification;
- customer and operational impact;
- Incident Commander / Support Lead / Domain Owner;
- containment decision;
- rollback decision and authority when applicable;
- sanitized logs, correlation/request IDs, manifest and SQL evidence;
- next update time;
- resolution state and closure evidence.

Secrets, passwords, JWTs and raw credentials must never enter evidence.

## Resolution

Close only after the affected source-of-truth signal is rechecked. Historical evidence must remain append-only where the domain contract requires it.

## Post-incident review template

- incident ID / severity / duration;
- detection and response timeline;
- user/business impact;
- root cause and contributing factors;
- what worked / what failed;
- data-integrity verification;
- rollback/recovery decision;
- SLO/error-budget impact;
- corrective actions with owner and due date;
- validator/test/runbook change required;
- follow-up verification evidence.

A PIR is mandatory for SEV1 and for recurring SEV2 incidents.
