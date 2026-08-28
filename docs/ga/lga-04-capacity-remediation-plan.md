# LGA-04 Capacity Remediation Plan

## Capacity boundary
The carried-forward capacity boundary is Railway/API concurrency under readiness pressure. LGA-04 probes concurrency and compares readiness p95 to the allowed threshold.

## Remediation options
1. Increase Railway service capacity or replicas.
2. Review PostgreSQL connection pooling and waiting connections.
3. Re-run LGA-04 after capacity changes.
4. Keep Limited GA until concurrency and readiness behavior are acceptable.

## Acceptance
A formal acceptance decision may keep Limited GA running, but it must not activate Public GA. Public GA requires separate manual approval after capacity evidence is clean.
