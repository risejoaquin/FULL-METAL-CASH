# LGA-08 — Continued Monitoring Checklist

Use this checklist when Railway capacity has not been upgraded and the existing formal limited-capacity acceptance remains active.

- [ ] LGA-07 PASS evidence exists.
- [ ] Limited GA scope remains max 2 stores.
- [ ] Public GA not activated.
- [ ] `PublicGaDecision = KEEP_LIMITED_GA`.
- [ ] Schema version 4 / `schema_version_4` confirmed.
- [ ] Health live and ready return 2xx.
- [ ] Authenticated observability reports database ready.
- [ ] Unauthenticated observability returns 401.
- [ ] Sync pending = 0.
- [ ] Sync processing = 0.
- [ ] Sync retry pending = 0.
- [ ] Sync conflict count <= 3; do not erase conflicts without formal decision.
- [ ] Dead letter count <= 1; do not erase dead letter evidence without formal decision.
- [ ] Negative stock count = 0.
- [ ] Waiting connections <= 12; do not raise the baseline.
- [ ] Long-running queries = 0.
- [ ] No duplicate local sales.
- [ ] No negative payments.
- [ ] RLS remains enabled on tenant tables.
- [ ] Recent sales >= 6 in the 24-hour decision window.
- [ ] Recent payments >= 6 in the 24-hour decision window.
- [ ] Recent receipts >= 3 in the 24-hour decision window.
- [ ] Dashboard and API read models remain reachable.
- [ ] WPF QSR sales flow visually confirmed.
- [ ] Capacity risk remains formally accepted while the capacity probe does not pass.
