# BETA-07 Daily Monitoring Checklist

- Health: live, ready, database ready.
- API: failed requests and p95 latency.
- Sync: processed, retry pending, retry due, retry over SLA, dead-letter, stale processing.
- Conflict: pending conflicts must be zero.
- Cash: open shifts reviewed; cash differences in last 24h must be zero.
- Sales: completed sales evidence; failed payments in last 24h must be zero.
- Inventory: negative inventory reviewed; low stock reviewed.
- Audit: audit events in last 24h must exist.
- Support: every condition gets an owner/triage path.
- Contract: schemaVersion 4 and schema_version_4.
