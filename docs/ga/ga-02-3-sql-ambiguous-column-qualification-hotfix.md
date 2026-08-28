# HOTFIX GA-02.3 — SQL Ambiguous Column Qualification

## Status
PENDING USER VALIDATION

## Failure observed
GA-02 reached the pre-remediation diagnostic after a fresh GA-01 PASS, then PostgreSQL stopped with:

`column reference "tenant_id" is ambiguous`

The ambiguity was in duplicate/idempotency diagnostic subqueries that joined `pos.sync_inbox_events i` with parameter CTE `p`, where both expose a `tenant_id` column.

## Correction
All columns in the affected duplicate-detection projections, predicates and `GROUP BY` clauses are explicitly qualified with alias `i`:

- `i.tenant_id`
- `i.terminal_id`
- `i.batch_id`
- `i.sequence_number`
- `i.event_id`

No decision thresholds, mutation behavior, schema contract, or production data semantics were changed.

## Scope audit
The related GA-02 remediation SQL was reviewed for the same ambiguity family. Its joined references are already explicitly qualified where multiple relations expose the same column names; no mutation was executed by the failed run.

## Safety
- No DELETE/TRUNCATE.
- No inventory ledger rewrite.
- No commercial entity mutation caused by this hotfix.
- `schemaVersion = 4` preserved.
- `syncContract = schema_version_4` preserved.
- General Availability remains not activated.
