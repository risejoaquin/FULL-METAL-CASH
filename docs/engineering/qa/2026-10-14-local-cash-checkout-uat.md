# SolidPOS / AC-DEV — QA-001/002/003, Windows local cash checkout

**Evidence date:** 2026-10-14 UTC (operator-submitted logs; exact local test clock not independently certified).
**Scope:** disposable local laboratory, Windows WPF + SQLite, one store, one terminal, one cash sale.
**Disposition:** PASS for the exact observed local cash sale and independent read-only reconciliation. **Not full manual MVP scenario signoff**, and **not commercial GO**.
**Operator:** user-confirmed manual execution. **Independent reviewer / physical evidence repository:** pending.
**Application SHA at initial WPF test:** approximately `4c7a2a9751fea58735d488f5da4372cff2ffc8b9` (last known checked-out `AC-DEV` before subsequent pull); not attested by an on-device SHA capture at the instant of purchase.
**SQL verification script at QA-002/003:** `ec97648003334ea146db012c54dbfcb597c17665`.
**Historical LGA-03 3-day test:** performed and passed manually per operator confirmation; distinct campaign, not re-executed here.

## Financial evidence — exact transaction

| Assertion | Recorded result |
| --- | --- |
| Sale ID | `45f0a935-8d08-47e7-b170-72797424d34c` |
| Closed cash shift | `dccd99d7-0f66-43fc-a148-97d1270714ce` |
| SKU | `AMERICANO-12`, quantity 1 |
| Opening cash | 10,000 MXN cents |
| Sale amount | 4,500 MXN cents |
| Tendered | 5,000 MXN cents |
| Change | 500 MXN cents |
| Expected and counted cash | 14,500 MXN cents |
| Cash difference | 0 MXN cents |
| Persistence | one sale, one line, one cash payment, one sale cash movement |
| Ledger | one opening movement; ledger equals counted cash |
| Sync outbox | one schema-v4 `sale.completed` event, status **pending (0)**; **NO SERVER ACK** |
| Print job | one job, status **pending**; **NO PHYSICAL TICKET VERIFIED** |
| SQLite integrity | `PRAGMA integrity_check = ok` |
| Evidence tool | `scripts/poscore/verify-closed-commercial-sale.py`, read-only, all assertions PASS |

## Recorded test sequence

1. Operator reported **6/6 PASS** for terminal association, login, shift opening, checkout from WPF, local write and expected cash.
2. Screenshots submitted in the conversation show `VENTA GUARDADA` with total 4,500 and change 500, and `CIERRE GUARDADO` with expected 14,500, counted 14,500 and difference 0.
3. The first verification command incorrectly searched *only an open shift* after the shift was already closed, so it returned `No local cash shift found`; its displayed QA-002 PASS banner was **invalid** and is not counted.
4. A corrected, independently executable SQLite **read-only** validator searches the closed shift, verifies financial rows, forbids duplicates, checks outbox and print job without attributing remote/physical completion.
5. Operator executed corrected script and supplied output containing `QA-002 / QA-003: PASS — LOCAL SQL FINANCIAL RECONCILIATION`. No financial rewrite, new sale or new shift was required.

## Mapping to manual acceptance matrix

| Criterion | Evidence contribution | Still missing for the *full* scenario |
| --- | --- | --- |
| `AUTH-05` | successful Windows local login | negative credentials, alternate operator/RBAC, logout, offline expiry, cross-store |
| `CAT-05` | one `AMERICANO-12` SKU from local catalog | multi-SKU, variants, inactive SKU and price-change cases |
| `SALE-05` | cash checkout, change, atomic local ledger, idempotent *single occurrence* verified | physical cash/ticket where required and intentional replay behavior |
| `CASH-04` | single open/close, exact counted balance, closed persistently | multiple sales, cash-in/out, difference, authorization edge cases |
| `PRN-03` | print job queued | real printer, disconnect/retry, physical ticket |
| `SYNC-04` | schema-v4 event queued | server ACK, processing, remote sale, replay/idempotency |
| `START-05` | WPF launched from SDK in Windows | installer on clean Windows machine |
| `REC-06` / `REC-07` | not executed in this campaign | controlled crash and independent supervisor reconciliation |

Do **not** change weights or mark any full manual scenario as complete based on this limited campaign alone. A full release requires complete evidence from the target build and authorization under `SOLIDPOS_MVP_UAT_EXECUTION.md`.

## Follow-up

- First verify remote ACK and server processing **only against localhost PosServer and disposable local PostgreSQL**.
- Separately perform physical print driver/peripheral testing and recovery.
- Audit remaining manual coverage with test IDs and the current commit hash.


## QA-004 follow-up: ACK/ingestion proven, processing still pending

Operator ran `scripts/poscore/verify-sale-sync-status.py` against the same AC-DEV Windows SQLite and the **local** `full-metal-cash` PostgreSQL environment. Evidence from console:

- Local sale `45f0a935-8d08-47e7-b170-72797424d34c`, **4,500 cents**.
- Local outbox event `9bd6bac8-45a2-42e6-87ff-29f48c9ce514`: `status=2`, synced at `2026-10-15T03:35:46.4618676+00:00`.
- Exactly one persisted local `acknowledged` row. Batch `35485a19-4732-4d17-ada0-cbc726cc17d5`.
- Remote PostgreSQL `sync_inbox_events`: **exactly one** matching `sale.completed`, `status=received`, created `2026-10-15T03:35:46.398638+00:00`, `processedAt=null`.
- Remote `pos.sales`: **zero** matching `local_sale_id` rows.
- `QA-004` result: **PASS for durable ACK / ingestion only; NOT PASS for end-to-end server sale processing**.

The subsequent user-run `sync-push` returned "No pending local outbox events to sync" and did not send a new event. A legacy QA script printed "ACK REMOTO CONFIRMADO" despite its initial expected-one-pending precondition failing; do **not** treat that banner as evidence of a new push.

**Technical dependency discovered:** `PostgreSqlSalesRepository.CreateAsync` requires a matching `pos.cash_shifts` row in `open` status for the store, terminal and operator before it creates the server sale. A local SQLite cash-shift opening is not by itself proof of a central open shift. Next action is **read-only preflight** for central shift and event contract, followed by controlled processing of the exact received batch only when prerequisites are satisfied. Never replay or modify the already-acknowledged local sale as an ad hoc workaround.

`PRN-03` remains pending because physical printing is not evidenced.
