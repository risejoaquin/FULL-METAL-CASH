# LGA-12 Evidence Matrix

| Evidence | Requirement | Blocking rule |
|---|---|---|
| Entry gate | LGA-11 reviewed PASS | Missing prerequisite blocks LGA-12 |
| Health | live/ready 2xx | API health failure blocks |
| Capacity | concurrency 3, 6 requests, p95 <= 1200 ms | Failed capacity requires upgrade recommendation |
| DB pressure | waiting <= 12, long-running = 0 | Exceeding baseline blocks |
| Commercial operations | sales >= 6, payments >= 6, receipts >= 3 in 24h | Below minimum blocks |
| Cash | closed shifts >= 1, open shifts = 0, cash differences = 0 | Regression blocks |
| Inventory | negative stock = 0 | Any negative stock blocks |
| Sync queues | pending/processing/retry = 0 | Any active queue blocks |
| Sync integrity | conflict <= 3, dead letter <= 1, legacy schema = 0 | Increase/regression blocks |
| RLS | missing tenant RLS tables = 0 | Missing RLS blocks |
| Audit | >= 1 event in 24h | No activity blocks |
| Release | stable release >= 1 | Missing release blocks |
| Schema | schema version 4, `schema_version_4` | Any legacy contract blocks |
| Public GA | activation false | Any activation blocks |
| Final decision | recorded and consistent with capacity | Inconsistent decision blocks |
