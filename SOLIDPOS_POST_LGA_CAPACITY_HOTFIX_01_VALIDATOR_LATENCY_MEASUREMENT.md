# POST-LGA Capacity Hotfix 01 — Validator Latency Measurement

## Cause
The concurrency probe used `Start-Job` and measured latency with a Stopwatch inside each new PowerShell job. Windows PowerShell job startup inflated measured p95 by seconds, affecting `/health/live` and `/health/ready` almost equally.

## Fix
The probe keeps the same `Start-Job` concurrency orchestration, but latency is now taken from `curl.exe -w %{time_total}`. This measures HTTP transfer time and excludes PowerShell job startup overhead.

## Invariants unchanged
- PublicGaReadinessConcurrency = 3
- ConcurrencyProbeRequests = 6
- MaxReadinessP95Ms = 1200
- AllowedWaitingConnectionCount = 12
- AllowedNegativeStockItemCount = 0
- schemaVersion = 4
- syncContract = schema_version_4
- PublicGaDecision = KEEP_LIMITED_GA
- Public GA remains NOT_ACTIVATED

## Decision
Re-run the same strict POST-LGA capacity validator. Do not alter thresholds based on the previous Start-Job-contaminated p95 values.
