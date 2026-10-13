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
4. Use an operator identity and incident/change-ticket reference. The storage API checks that `Actor`, `Reason` and the live `ApprovalReference` are present and bounded. **It does not authenticate the operator or verify the approval.** That must be implemented in the calling UI/service before live use.
5. Do not connect the workflow to Railway, production PostgreSQL, live terminals or real payment providers during development.

## Sequence (manual only)

1. **Inspect:** use `GetLocalSyncQueueHealthAsync` and confirm stale `InFlight` count. Select a conservative timeout (allowed: **5 minutes through 24 hours**). The default minimum is a safety floor, not a recommendation to reclaim while a network request may still be in progress.
2. **Preview:** call `IControlledLocalSyncRecoveryRepository.RecoverStaleClaimsWithAuditAsync` with `DryRun=true`, a new `RecoveryId`, and explicit `TenantId`, `StoreId`, `TerminalId`. Specify `MaxEvents` between **1 and 100**.
3. **Review:** check the returned eligible count, listed event IDs and `HasMoreEligible`; reconcile the terminal scope and remote server before approving. The preview creates audit records but **does not** release any claim.
4. **Authorize:** obtain an approved change or incident reference from an independent authorized reviewer. Use a new `RecoveryId` for execution; IDs are one-use even for previews.
5. **Execute:** invoke the same API with `DryRun=false` and the verified approval reference. SQLite changes the selected events from `InFlight` to `Pending`, removes only their existing claim rows, and writes a **transactional** audit of actor, reason, approval reference, scope, timeout, selected event IDs and counts.
6. **Verify:** check the returned `RecoveredCount`, `HasMoreEligible`, new queue-health data, and the durable `local_sync_recovery_runs` / `local_sync_recovery_candidates` journal. Resume a single normal sync worker only when safe. If more events remain, repeat the **preview–review–approve–execute** sequence for a new bounded batch.
7. **Stop on failure:** a failed audit write rolls back the status changes and releases. A stale owner cannot confirm a newly acquired claim. Do not disable SQLite FK/transaction checks or purge claim-ID history to bypass conflicts.

## Invariants

- One explicit terminal scope per request. No automatic timer, background service or production connection is created here.
- Never mark an event `Synced` during stale-claim recovery. Only verified remote ACKs can do that.
- The historical `EventId` remains the remote idempotency key. A new `RecoveryId` identifies each manual decision, not a new sale.
- `local_sync_claim_history` prevents reuse of old claim IDs, and `local_sync_recovery_runs` / `local_sync_recovery_candidates` preserve local accountability.
- `RecoverStaleClaimsAsync` from `ILeasedLocalSyncRepository` remains a **legacy low-level compatibility method** that does not provide scoped operator approval/audit. Do not expose it as an operator action. Replacing existing low-level callers must be a reviewed follow-up before enabling live recovery.
- PostgreSQL, WPF hardware and remote idempotency are outside this Windows SQLite unit-test gate.

## Verification gates before enabling in a real POS terminal

Implement authenticated operator authorization and independently verified approvals at the application boundary, route all operator recovery flows through the controlled API, restrict/remove the legacy bypass from production paths, validate server-side idempotency with a disposable PostgreSQL service, and perform UAT of crash/restart scenarios. Do not merge or deploy without PO GO/NO-GO.
