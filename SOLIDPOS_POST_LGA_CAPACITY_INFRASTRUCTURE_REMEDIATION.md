# SolidPOS - Post-LGA Capacity / Infrastructure Remediation

## Status

Implementation package prepared. Production PASS is not claimed until deployment and strict validation logs are reviewed.

## Change

Optimized `PostgreSqlReadinessProbe` from 12 database commands per `/health/ready` request to one catalog query while preserving all required-table checks.

## Reason

LGA-12 closed Limited GA successfully but retained the Public GA capacity blocker. The latest validated boundary was concurrency 3 with `/health/live` p95 3279 ms and `/health/ready` p95 1435 ms against a 1200 ms target.

## Guardrails preserved

- Public GA NOT_ACTIVATED.
- Limited GA retained.
- schemaVersion = 4.
- sync contract = `schema_version_4`.
- negative stock = 0.
- waiting connections maximum = 12.
- conflicts <= 3.
- dead letters <= 1.
- concurrency target remains 3.
- p95 target remains 1200 ms.

## Decision after deployment

PASS means the capacity gate is technically satisfied and a Public GA readiness review may be performed. It does not activate Public GA.

FAIL means infrastructure remediation remains required. Do not loosen thresholds or baselines.
