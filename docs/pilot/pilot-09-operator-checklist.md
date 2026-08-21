# PILOT-09 Operator Checklist

## Detect

- Check `/health/live` and `/health/ready`.
- Check PosDashboard Operations Monitoring.
- Check `/api/v1/observability/metrics`.
- Check `/api/v1/sync/status`, `/api/v1/sync/dead-letter`, and `/api/v1/sync/conflicts`.

## Classify

- SEV1: readiness failure, database unavailable, auth incident, cash drawer blocking incident, backup/restore/rollback incident.
- SEV2: terminal enrollment incident, offline terminal incident, sync backlog, retry_pending growth, dead_letter incident, sync conflict incident, inventory inconsistency.
- SEV3: receipt generation incident or non-blocking dashboard/report delay.

## Contain

- Freeze deploys for SEV1.
- Preserve tenant/store/terminal/user evidence.
- Keep terminal offline only when within max offline hours and operator authorization is valid.
- Do not manually edit production data without scoped rollback plan.

## Recover

- Use the documented endpoint/runbook action.
- Retry sync/dead-letter only after payload and cause are understood.
- Resolve conflicts with use_server or use_client only after comparing evidence.
- Use backup/restore/rollback drill from PILOT-08 for destructive or schema incidents.

## Verify

- Re-check health/readiness.
- Re-check observability metrics.
- Re-check SQL scoped counts.
- Confirm audit trail or log artifact exists.

## Close

- Write incident status.
- Record GO/NO-GO.
- Attach PowerShell output and generated log.
