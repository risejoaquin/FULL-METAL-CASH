# Public GA Stability Burn-In

Purpose: verify that SolidPOS remains stable after real Public GA activation across multiple consecutive production samples.

This gate is read-only. It does not activate, deactivate, migrate, or modify production state.

Pass contract:
- Public GA remains ACTIVATED and rolloutStage remains public_ga.
- schemaVersion remains 4 and syncContract remains schema_version_4.
- At least 3 consecutive samples pass.
- Each sample runs 6 requests at concurrency 3 against both health endpoints.
- p95 must remain <= 1200 ms.
- waiting connections <= 12; long-running queries = 0.
- negative stock = 0.
- sync pending/processing/retry = 0; accepted conflicts <= 3; accepted dead letter <= 1.
- RLS, financial integrity, operational activity, open-shift and cash-difference guardrails remain clean.

PASS authorizes final Public GA production closure. It does not imply the software lifecycle ends; maintenance and future releases continue after closure.
