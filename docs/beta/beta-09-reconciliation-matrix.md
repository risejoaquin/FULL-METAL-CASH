# BETA-09 Reconciliation Matrix

| Domain | Reconciliation | GO condition |
|---|---|---|
| sales reconciliation | accepted sales vs approved payments; receipt linkage; returns vs approved refunds | no mismatches or orphan active receipts |
| cash reconciliation | recent closed-shift differences and open shift age | no cash difference in last 24h; no stale open shift |
| inventory reconciliation | inventory_ledger derived stock after EXP-06 | zero negative inventory |
| catalog/pricing consistency | product prices, price windows, tax modes, modifier semantics | zero invalid rows |
| customer/user consistency | active users, customer dataset, role/store access references | no orphan role/store access references |
| sync consistency | schema version, conflicts, dead-letter baseline | schema 4 only; no unresolved conflicts; no new/untriaged dead-letter |
| audit consistency | tenant audit evidence | audit evidence exists, including last 24h |
| release consistency | active beta Velopack release | valid signature, rollback version, non-mandatory beta release |

All blockers must be empty before GO BETA-10.
