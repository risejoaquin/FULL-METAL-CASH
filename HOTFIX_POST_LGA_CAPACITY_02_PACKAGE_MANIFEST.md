# POST-LGA Capacity Hotfix 02 Package Manifest

- Scope: restore validator execution contract after latency-measurement hotfix.
- Root cause: Hotfix 01 replaced the concurrency-probe function with an overly broad text range and accidentally removed the validator main execution body. As a result, `$db` was null when the blocker matrix attempted to read `tenantState`.
- Fix: restore the complete main execution body from the last known-good POST-LGA validator while retaining the curl-based HTTP latency probe.
- Validator version: POST-LGA-CAPACITY.0.2-validator-latency-contract-restoration
- Thresholds unchanged: concurrency 3, requests 6, max p95 1200 ms, waiting connections 12, negative stock 0.
- Public GA remains NOT_ACTIVATED / KEEP_LIMITED_GA.
