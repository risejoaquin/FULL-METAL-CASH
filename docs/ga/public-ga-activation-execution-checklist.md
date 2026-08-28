# Public GA Activation Execution Checklist

- Public GA Readiness Review PASS.
- Public GA Activation Decision PASS / GO APPROVED.
- Explicit operator activation instruction received.
- Build/test/security gate revalidated through activation-decision prerequisite unless explicitly skipped with reviewed logs.
- `/health/live` and `/health/ready`: concurrency 3, six requests, p95 <= 1200 ms.
- Waiting connections <= 12.
- Long-running queries = 0.
- Negative stock = 0.
- schemaVersion = 4.
- syncContract = `schema_version_4`.
- Transactional state write verified.
- Postflight health/capacity verified.
- Automatic rollback available.
- Public GA activation manifest retained.
