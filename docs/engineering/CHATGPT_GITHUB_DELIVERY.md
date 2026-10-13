# SolidPOS / FULL-METAL-CASH — ChatGPT → GitHub → GitHub Actions

**Adoption date:** 2026-10-13. **Status:** proposed workflow, under draft PR; **no deployment authorized**. Active ChatGPT development branch: **`AC-DEV`**. This changes the delivery process, not SolidPOS's product architecture.

## 1. Objective

Let the PO direct development entirely in ChatGPT. The assistant inspects GitHub, edits a bounded feature in a separate GitHub branch, adds tests, commits, opens/updates a draft PR, and checks GitHub Actions evidence. The PO need not run a local development environment or install an orchestrator for this workflow. ChatGPT itself does **not** run indefinitely between chat turns; a separate explicitly scheduled task is required for recurring autonomous reviews.

## 2. Verified repository baseline (2026-10-13)

- Repository: `risejoaquin/FULL-METAL-CASH` (PUBLIC). Default branch: `main`; audited HEAD `c9a7c2de8c1c4222799b16ccdf9134c4d77079fa`.
- App: .NET 8 ASP.NET Core PosServer, PosCore (SQLite offline-first, WPF client), PostgreSQL, PosDashboard React, PosBuilder WPF.
- Existing CI workflow: `.github/workflows/solidpos-ci.yml`, event triggers `push`, `pull_request` and `workflow_dispatch`. Windows runner: secret scan; V1.1 static contract validators; dashboard `npm ci` / `self-test` / `build`; `dotnet restore`, `build`, `test`; TRX artifacts. The most recent inspected run on `main` was successful on 2026-09-16: https://github.com/risejoaquin/FULL-METAL-CASH/actions/runs/35073073377 . A historical PASS is not a PASS for a new SHA.
- On 2026-10-13 `main` reported `protected: false`. Required checks / PR protection MUST be configured by a repository administrator; writing instructions here does not enforce GitHub branch protection.
- Railway production project `heroic-solace` has two GitHub-linked `main` services: `FULL-METAL-CASH` API and `cooperative-connection` dashboard. In each Railway service Settings → Source, explicitly click **Disable** for GitHub Autodeploy; keep existing deployed services and source connection intact. The Railway connector currently exposes inspection but not the Disable Autodeploy toggle, so this manual action remains **unverified** until read back in the Railway dashboard. See https://docs.railway.com/deployments/github-autodeploys#disable-automatic-deployments.
- `SOLIDPOS_TEST_POSTGRES` gates some integration tests. When unset, tests can return early; green `dotnet test` alone does not prove PostgreSQL integration.
- Historical repository docs mention active Railway production API and dashboard. Their runtime status was **not** verified here. No production access, health checks, credentials or connections are part of this adoption.
- Existing `railway.toml`/deployment assets remain untouched. A GitHub CI result alone does not prove whether a third-party auto-deploy webhook is linked; verify service-side deployment triggers before any future release.

## 3. Required workflow, step by step

1. **Intake**: PO supplies scope/outcome and acceptance conditions; assistant checks existing branches, current contracts, any open PR, current baseline and dependencies.
2. **Design**: choose a small reversible increment; identify relevant invariants (tenants/RBAC, cash reconciliation, inventory, synchronization, offline, migrations, update channels); surface any ADR/decision required.
3. **Code**: create/use a named task branch based on the current approved base SHA; perform only scoped edits, regression tests and documentation, without overwriting parallel work.
4. **CI**: let existing `SolidPOS CI` run on `AC-DEV` push/PR. Inspect the result for the exact commit SHA, checks, test reports and failure logs. Retry only after understanding failures.
5. **Review**: deliver a draft PR. Explain change, validation, test skips, untested desktop/hardware paths, security impact and rollback.
6. **Approval**: PO reviews. Even green QA is NOT permission to merge or publish; a separate authorization is required. Never bypass configured review rules.
7. **Release**: a distinct future phase, under explicit PO GO/NO-GO, with production DB migration review, rollback, backups, RPO/RTO, deployment and health checks.

## 4. Isolation rules

### Local/CI only during coding

- Backend: run .NET in GitHub Actions.
- Dashboard: build + execute existing self-tests in Actions.
- POS desktop: build/test WPF projects on GitHub-hosted Windows runners; a successful headless build cannot prove touchscreen UX, peripherals, printer, scanner, cash drawer or field behavior.
- SQLite: use actual disposable SQLite DBs/fixtures as the default storage test path in the Windows CI runner, not real registers or live sales. `SQLiteLocalPersistenceTests.cs` exercises initialization, WAL, foreign keys and persistence without external services.
- PostgreSQL: central PosServer remains PostgreSQL in production. SQLite is not a faithful replacement for PostgreSQL schemas, migrations or RLS; those checks are reported as skipped until a separately authorized disposable PostgreSQL runner exists. The existing helper drops `pos` schema. Never point `SOLIDPOS_TEST_POSTGRES` to a real remote instance.
- Payments, hardware, receipts and network-sync edge cases: mocks/contract tests unless the PO separately authorizes controlled real-device/UAT tests.
- Railway and production Supabase/PostgreSQL: release-only. Do not modify live resources, credentials, environment variables or hosted services.

### Contracts that must be preserved

The documented `schemaVersion: 4` and `syncContract: schema_version_4` are frozen until reviewed. Maintain backwards compatibility; changing cash, inventory, tenant boundary, auth or data migrations requires targeted tests and an explicit design decision when behavior changes.

## 5. What CI proves — and what it does not

| Gate | Available today | Additional follow-up |
| --- | --- | --- |
| Windows .NET compile/tests | Existing Actions workflow | Review new SHA's logs and TRX |
| React dashboard build/self-test | Existing Actions workflow | Add real browser E2E for critical flows |
| Security/static checks | Existing workflow | Evaluate dependencies, secrets and high-risk paths |
| PostgreSQL/RLS/migrations | Test code exists; optional execution | Design dedicated ephemeral DB CI job; guard against parallel schema resets and use current migration chain |
| Offline sync resilience | Code/tests and existing contract validators | Extend conflict, retry, idempotency and recovery tests |
| WPF and physical peripherals | Compile/unit/static checks only | Windows interactive/hardware acceptance later |
| Commercial production release | Not part of development CI | Explicit PO GO, staging/production gates and operational runbooks |

Do not mark `PostgreSQL PASS`, `WPF hardware PASS`, `E2E PASS` or `release ready` unless each corresponding check was genuinely executed and evidenced.

## 6. Required repository administrator action

Protect `main` using GitHub Settings → Rules → Rulesets (or Branch protection rules):
- Require pull request before merge, approvals by an eligible reviewer and resolution of conversations.
- Require **SolidPOS CI / Build and test (.NET 8 / Windows)** (confirm exact current check name in Actions before entering it), branch up-to-date if appropriate, and no bypass except documented break-glass administration.
- Prevent force pushes and deletions on `main`.
- Review whether existing Railway deployments automatically track `main` and ensure no development PR/head commits deploy on their own. Do not flip deployment settings or secrets without separate PO approval.
- Limit `GITHUB_TOKEN` scopes. Current CI specifies `permissions: contents: read`; preserve that minimum for QA.

**Note:** branch protection cannot be enforced by markdown, PR templates or agent instructions alone. A repository admin must perform and verify these platform settings.

## 7. First technical waves after adoption

1. Baseline current SHA: run and audit existing SolidPOS CI without touching production, including logs and skipped test report.
2. Add isolated PostgreSQL test workflow/job with a fresh DB and serialized schema-mutating tests; verify full migration inventory against current `database/postgresql` and smoke tests.
3. Expand frontend/browser journey tests for login/tenant separation/orders/inventory/cash and offline recovery; isolate backend mocks.
4. Add Windows desktop smoke automation where feasible; reserve real peripherals for a later hardware gate.
5. Review package version drift, security scanning and reproducible builds. No dependency or contract upgrade merely for novelty.

## 8. SQLite outbox concurrency and crash recovery (AC-DEV only)

SQLite's internal `local_sync_claims` table is a **local additive implementation detail**; it does not change the remote `schemaVersion=4` or `syncContract=schema_version_4`. The PosServer PostgreSQL model is unchanged. Initialization uses `CREATE TABLE IF NOT EXISTS` for existing disposable and developer SQLite files.

- The real `SQLiteLocalPosRepository` now implements `ILeasedLocalSyncRepository`. `ClaimPendingOutboxEventsAsync` reserves one tenant/store/terminal-scoped batch with `BEGIN IMMEDIATE` semantics and persists the `InFlight` state and the owning `claim_id` atomically. A second worker sees no claimable event from that batch while it is in-flight.
- Every remote acknowledgement is applied with **owner verification** and an atomic update of `Synced` plus its acknowledgement journal. Retry/dead-letter transitions likewise require the owning claim.
- An unsuccessful or ambiguous response releases any still-owned events to `Pending`; remote timeouts follow the retry policy. A late worker **cannot** commit or release a claim recovered and acquired by another worker.
- If a process dies before cleanup, an external recovery step must invoke `RecoverStaleClaimsAsync(nowUtc, processingTimeout)` after a conservative timeout selected for the deployment. **There is deliberately no hidden automatic recovery timer**. Do not recover claims that may still be actively processed. Since slow original workers might still have reached the server, event-level server idempotency remains a required independent safeguard.
- The existing non-leased `ILocalPosRepository` contract still supports older in-memory fakes; it does not offer the same concurrency guarantees. The SQLite test suite covers two workers, scope isolation, expired-claim recovery, fencing of stale owners, malformed remote responses and ACK transactions.
- QA remains restricted to the disposable Windows Actions runner. No branch merge, Railway changes, production data or external WhatsApp/payment providers were involved.

## 9. Report format for each ChatGPT increment

```text
Task:
Acceptance conditions:
Branch / draft PR:
Base SHA / resulting SHA:
Files changed:
CI run for resulting SHA:
Build / unit / contract / integration / browser:
Skipped gates and why:
Security, tenants, cash, inventory, offline impact:
Manual action or PO decision (if required):
Next bounded task:
```

**Definition of done:** change implemented, regression tests added, relevant checks green on the exact SHA and reviewed as a draft PR. An approved merge or production deploy remains a **separate decision**.
