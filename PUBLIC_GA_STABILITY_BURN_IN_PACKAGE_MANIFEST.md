# Package Manifest — Public GA Stability Burn-In

- Validator: `PUBLIC-GA-STABILITY-BURN-IN.1.0-multi-sample-production-stability`
- Read-only production stability gate.
- Minimum samples: 3.
- Default interval: 30 seconds.
- Capacity target unchanged: concurrency 3, 6 requests, p95 <= 1200 ms.
- DB pressure threshold unchanged: waiting connections <= 12.
- Public GA must remain ACTIVATED.
- schemaVersion 4 and syncContract schema_version_4 are immutable for this gate.
- PASS next state: `FINAL_PUBLIC_GA_PRODUCTION_CLOSURE_AUTHORIZED`.
