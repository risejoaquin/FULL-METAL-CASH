# GA-01 Evidence Matrix

| Evidence | Source | Requirement |
|---|---|---|
| Fresh BETA-10 PASS | inherited BETA-10 validator + manifest | mandatory |
| Repository baseline SHA-256 | deterministic repository hash | mandatory |
| Tenant/store/terminal/users | GA-01 SQL snapshot | mandatory |
| Catalog/modifier semantics | GA-01 SQL snapshot | mandatory |
| Sales/payments/receipts | GA-01 SQL snapshot | mandatory |
| Returns/refunds/cash | GA-01 SQL snapshot | mandatory |
| Inventory | `inventory_ledger` aggregate | mandatory |
| Sync/schema/dead-letter/conflict | GA-01 SQL snapshot | mandatory |
| Audit | GA-01 SQL snapshot | mandatory |
| Release/update | GA-01 SQL snapshot | mandatory |
| Inherited conditions | fresh BETA-10 + GA-01 SQL | mandatory |
| GA activation state | manifest + contract | must be false |
| Runtime snapshot | `.runtime/ga-01-general-availability-baseline-freeze/ga-01-snapshot.json` | mandatory |
| Runtime evidence | `.runtime/ga-01-general-availability-baseline-freeze/ga-01-evidence.md` | mandatory |
| Runtime manifest | `.runtime/ga-01-general-availability-baseline-freeze/ga-01-manifest.json` | mandatory |
