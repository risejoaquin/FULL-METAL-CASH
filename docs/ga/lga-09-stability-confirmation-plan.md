# LGA-09 Stability Confirmation Plan

## Entry gate

LGA-08 must already be PASS in real production with `CONTINUE LIMITED GA`.

## Checks

1. Restore, build and test the full solution.
2. Run local secret scan and confirm WPF QSR command-state integrity.
3. Authenticate against production without printing credentials or authorization headers.
4. Check `/health/live` and `/health/ready`.
5. Confirm protected observability remains authenticated and database-ready.
6. Confirm sync queues are operational and schema version 4 remains current.
7. Confirm inventory API and reporting/dashboard read models.
8. Run concurrency 3 with six requests against health live and health ready and capture p95.
9. Capture PostgreSQL stability snapshot: waiting connections, long-running queries, RLS, sales/payments/receipts/audit, sync integrity, inventory and rollout scope.
10. Apply blocker matrix and explicit capacity decision.

## Stability thresholds

- `MaxReadinessP95Ms = 1200`
- `PublicGaReadinessConcurrency = 3`
- `ConcurrencyProbeRequests = 6`
- `AllowedWaitingConnectionCount = 12`
- negative stock = 0
- sync legacy schema events = 0
- retry pending = 0
- stale processing = 0
- duplicate local sales = 0
- negative payments = 0
- RLS missing tenant tables = 0
- long-running queries = 0

## Limited GA policy

If concurrency 3 remains above the capacity threshold, the correct outcome is `CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA` while Limited GA continues under formal capacity acceptance.

Public GA NOT ACTIVATED throughout this plan.
