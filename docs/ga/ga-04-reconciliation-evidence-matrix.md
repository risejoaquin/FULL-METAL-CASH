# GA-04 Reconciliation Evidence Matrix

| Domain | Evidence | Material blocker |
|---|---|---|
| sales/payments | sale line totals, approved payments, paid/change | any mismatch |
| receipts | sale link, public token integrity, uniqueness | any invalid/orphan active receipt |
| returns/refunds | line totals, approved refunds, original sale | any mismatch/orphan |
| cash | open/stale shifts, counted vs expected, difference formula | any open/stale/formula mismatch or recent difference |
| inventory | ledger aggregate, references, negative stock | any invalid reference/negative disallowed stock |
| catalog/pricing | prices, windows, tax, modifier semantics | any invalid record |
| users/access | roles and store access | any orphan/inactive access relationship |
| sync/audit | retries, conflicts, schema v4, dead-letter delta, audit | any regression |

All evidence is read-only and sourced from PostgreSQL production source-of-truth.
