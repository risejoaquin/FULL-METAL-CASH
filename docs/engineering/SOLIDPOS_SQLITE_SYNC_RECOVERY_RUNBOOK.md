# SolidPOS — Controlled local outbox recovery

**Status:** proposed on `AC-DEV` draft PR only. **No release or deployment authorization.**

## Why this exists

An outbox event can remain `InFlight` if its owner disappears after claiming it.
A remote server might already have processed it. Recovering the event must preserve
its stable `EventId`, otherwise a replay could create a duplicate sale.
This procedure is for disposable SQLite QA and future operator-controlled recovery.

## Preconditions and operational authority

1. Establish the intended **tenant, store and terminal IDs** from an authorized local terminal session. Do not use a cross-tenant wildcard.
2. Establish that the previous sync worker has actually terminated. A five-minute timeout alone **does not prove worker death**. Stop/reconcile competing workers before a manual recovery.
3. Confirm that the remote sync server deduplicates the **same event ID** when the prior response was lost. Without that guarantee, stop and escalate; do not retry potentially paid/settled sales.
4. Authenticate **two distinct active local users** and check their tenant/store scope. The executor needs the local-only `sync.recovery.execute` permission; the approving reviewer needs `sync.recovery.approve`. The SQLite storage implementation validates session status, expiry, user activity, permission, and tenant/store membership in the write transaction. Neither user may approve their own execution. These permissions are not yet provisioned by the PosServer role-management contract.
5. A change-ticket string alone **is not an approval**. Approval must be registered by the independent session via `RegisterRecoveryApprovalAsync` for the exact `RecoveryId`, tenant/store/terminal scope, timeout, max events, reason, and reference; it expires in 15 minutes and is single-use. An expired/closed reviewer session or revoked reviewer permission invalidates it.
6. `Actor` remains display text only: the authoritative executor user/session are written to `local_sync_recovery_executors`. The API assumes that only authenticated code can obtain an authorized session ID and only trusted code provisions local users and permissions. OS-level tampering with a writable SQLite file is not in scope.
7. Do not connect the workflow to Railway, production PostgreSQL, live terminals or real payment providers during development.

## Sequence (manual only)

1. **Inspect:** use `GetLocalSyncQueueHealthAsync` and confirm stale `InFlight` count. Select a conservative timeout (allowed: **5 minutes through 24 hours**). The default minimum is a safety floor, not a recommendation to reclaim while a network request may still be in progress.
**Time-source safeguard:** the recovery cutoff is calculated from the trusted local system UTC clock, not the request's supplied timestamp. A request timestamp differing by over **2 minutes** is rejected, including attempts to fast-forward the clock to release healthy events. Before performing any live action, validate the computer's secure time synchronization; local clock compromise is outside the SQLite-only threat model.

2. **Preview:** call `IControlledLocalSyncRecoveryRepository.RecoverStaleClaimsWithAuditAsync` with `DryRun=true`, a new `RecoveryId`, an active `ExecutorSessionId`, and explicit `TenantId`, `StoreId`, `TerminalId`. The executor must have `sync.recovery.execute`. Specify `MaxEvents` between **1 and 100**.
3. **Review:** check the returned eligible count, listed event IDs and `HasMoreEligible`; reconcile the terminal scope and remote server before approving. The preview creates audit records but **does not** release any claim.
4. **Authorize:** obtain independent review and use the reviewer's authenticated `ApproverSessionId` to call `RegisterRecoveryApprovalAsync` with the exact request parameters and external incident/change reference. Use a **new `RecoveryId`** for execution; IDs are one-use even for previews. A second local user must hold `sync.recovery.approve`; never accept a bare string typed by the executor as proof.
5. **Execute:** invoke the same API with `DryRun=false`, the executor's active `ExecutorSessionId` and the pre-registered approval reference. The storage layer rechecks **both** users' permission and session status, approval expiration and exact scope, then consumes the approval in the same transaction as event changes. SQLite changes the selected events from `InFlight` to `Pending`, removes only their existing claim rows, and writes a **transactional** audit of the verified executor identity, approved reviewer identity, reason, approval reference, scope, timeout, selected event IDs and counts.
6. **Verify:** check the returned `RecoveredCount`, `HasMoreEligible`, new queue-health data, and the durable `local_sync_recovery_runs` / `local_sync_recovery_candidates` journal. Resume a single normal sync worker only when safe. If more events remain, repeat the **preview–review–approve–execute** sequence for a new bounded batch.
7. **Stop on failure:** a failed audit write rolls back the status changes and releases. A stale owner cannot confirm a newly acquired claim. Do not disable SQLite FK/transaction checks or purge claim-ID history to bypass conflicts.

## Invariants

- One explicit terminal scope per request. No automatic timer, background service or production connection is created here.
- Never mark an event `Synced` during stale-claim recovery. Only verified remote ACKs can do that.
- The historical `EventId` remains the remote idempotency key. A new `RecoveryId` identifies each manual decision, not a new sale.
- `local_sync_claim_history` prevents reuse of old claim IDs. `local_sync_recovery_runs` / `local_sync_recovery_candidates` / `local_sync_recovery_executors` / `local_sync_recovery_approvals` preserve the recovery decision, signer, executor, and event trace.
- The old unapproved `RecoverStaleClaimsAsync` entry point has been **removed from the public `ILeasedLocalSyncRepository` interface** and changed to `internal` on the SQLite repository, with test assembly access only. Operator recovery uses `IControlledLocalSyncRecoveryRepository`.
- PostgreSQL, WPF hardware and remote idempotency are outside this Windows SQLite unit-test gate.

## Verification gates before enabling in a real POS terminal

Integrate authenticated UI/service invocation with a trusted current session ID, provision the **new local-only permission codes** via the reviewed central role-sync process, verify the user's remote authority and freshness of cached privileges, prevent local DB tampering, validate idempotency with a disposable PostgreSQL service, and perform controlled UAT for crash/restart scenarios. Do not merge or deploy without PO GO/NO-GO.
