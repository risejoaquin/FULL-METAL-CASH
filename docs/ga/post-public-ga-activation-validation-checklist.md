# Post-Public-GA Activation Validation Checklist

- Public GA flags persisted true.
- rolloutStage = public_ga.
- /health/live and /health/ready HTTP 200.
- Concurrency 3, requests 6, p95 <= 1200 ms.
- waiting connections <= 12.
- long-running queries = 0.
- negative stock = 0.
- sync pending/processing/retry = 0.
- conflicts <= 3; dead letter <= 1.
- cash differences = 0; open shifts = 0.
- sales/payments/receipts/audit activity meets baseline.
- RLS present.
- schemaVersion = 4; syncContract = schema_version_4.
- blockerCount = 0.
