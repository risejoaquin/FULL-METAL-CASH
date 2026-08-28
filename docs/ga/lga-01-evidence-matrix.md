# LGA-01 Evidence Matrix

| Evidence | Required state |
|---|---|
| CGA-04 entry gate | PASS KEEP LIMITED GA / PUBLIC GA NOT ACTIVATED |
| Build/test | PASS |
| Secret scan | PASS |
| WPF QSR command enablement | FIXED_RAISE_CAN_EXECUTE_CHANGED |
| Sync pending/processing/retry | 0 |
| Sync conflict baseline | <= 3 |
| Dead letter baseline | <= 1 |
| Negative stock baseline | <= 1 unless adjusted |
| RLS drift | 0 |
| Duplicate local sales | 0 |
| Public GA | NOT_ACTIVATED |
