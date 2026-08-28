# LGA-04 Evidence Matrix

| Area | Evidence |
| --- | --- |
| LGA-03 | Final burn-in PASS / GO LGA-04 |
| Build/test | dotnet restore/build/test PASS |
| Security | local secret scan PASS |
| API | health, readiness, observability, sync, inventory, reports |
| Capacity | readiness concurrency probe and p95 |
| Database | SQL snapshot for tenant, sync, financial, inventory, RLS, DB pressure |
| Decision | keep limited GA or ready for public GA decision package |

Public GA not activated is a mandatory condition.
