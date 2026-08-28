# CGA-02 Monitoring Schedule

## Runtime sampling

The validator uses:

- `SampleCount`
- `SampleIntervalSeconds`
- `MonitoringWindowHours`

The default run is a bounded snapshot gate. It does not sleep for 24h/72h. The monitoring window is represented through the database and API snapshots and can be rerun during the real operating window.

## Required checks per sample

- `health/live`
- `health/ready`
- `observability/metrics` without auth returns 401
- `observability/metrics` with auth returns database ready
- `sync/status` has pending/retry/conflict counts at 0
- `sync/contract` remains schemaVersion 4
- `reports/sales/range` returns 200
- `reports/dashboard/overview` returns 200 with required query contract
- dashboard URL returns 2xx/3xx

## Frequency guidance

For manual operations outside the validator:

- first 2 hours: every 30 minutes
- after stabilization: every 2 to 4 hours
- after 24h PASS: extend to 72h if Public GA is still not activated
