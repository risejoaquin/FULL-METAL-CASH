# Post-LGA Capacity / Infrastructure Remediation

## Entry gate

LGA-12 is closed as PASS under `CONTINUE_LIMITED_GA`, with Public GA not activated. The remaining blocker is capacity at the Public GA readiness boundary.

## Objective

Remove the capacity blocker without weakening existing production guardrails:

- concurrency 3
- 6 probe requests
- `/health/live` p95 <= 1200 ms
- `/health/ready` p95 <= 1200 ms
- waiting connections <= 12
- negative stock = 0
- schema version 4 / `schema_version_4`
- Limited GA remains active while Public GA remains NOT_ACTIVATED

## Code remediation

`PostgreSqlReadinessProbe` previously performed one connectivity query plus one `to_regclass` query for every required table. With 11 required tables this produced 12 database commands for each `/health/ready` request.

The remediated implementation performs one PostgreSQL catalog query using `unnest(@required_tables::text[])` and `to_regclass(...)`. A successful connection plus successful catalog query proves connectivity and checks all required runtime tables in one SQL round trip.

No required table, readiness condition, tenant isolation rule, schema contract, or Public GA gate is removed.

## Infrastructure decision

Deploy and measure this code optimization first. If the strict capacity validator still fails, the next remediation is infrastructure-side: increase Railway compute/resources and review PostgreSQL pool/connection pressure. Do not raise the accepted waiting-connection baseline to manufacture a PASS.

Public GA activation is outside this package.
