# Post-LGA Capacity Validation Checklist

- LGA-12 reviewed PASS evidence exists.
- Readiness uses one catalog query, not one query per required table.
- `/health/live` returns 2xx.
- `/health/ready` returns 2xx.
- concurrency 3, 6 requests.
- live p95 <= 1200 ms.
- ready p95 <= 1200 ms.
- waiting connections <= 12.
- long-running queries = 0.
- negative stock = 0.
- sync queues: pending = 0, processing = 0, retry = 0.
- accepted conflict baseline <= 3.
- accepted dead-letter baseline <= 1.
- commercial operations remain valid: sales, payments, receipts, audit and shifts.
- schema version 4 and `schema_version_4` remain authoritative.
- Public GA not activated.
