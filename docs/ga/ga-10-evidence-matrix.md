# GA-10 — Evidence Matrix

| Evidence | Required result | Source |
|---|---|---|
| GA-09 prerequisite | PASS / GO GA-10 | runtime manifest or fresh revalidation |
| Known capacity condition | documented | roadmap + GA-10 docs |
| Build/test | PASS | dotnet |
| Secret scan | PASS | scripts/security |
| health/live | 200 | production API |
| health/ready | 200 | production API |
| metrics without auth | 401 | production API |
| metrics with auth | 200 + required sections | production API |
| sync contract | schema 4 | production API |
| dashboard overview endpoint | 200 | production API |
| dashboard build/self-test | PASS or documented skip | local dashboard validator |
| alert thresholds | documented | ga-10-alerting-thresholds-and-routing.md |
| on-call runbook | documented | ga-10-oncall-dashboard-runbook.md |
| DB pressure | no waiting locks / no long queries | SQL check |
| RLS | no missing RLS tenant tables | SQL check |
| GA activation | False | manifest |
