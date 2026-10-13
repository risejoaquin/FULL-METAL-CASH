# ADR-AC-DEV-RECOVERY-RBAC — Central permission registration with explicit grants

**Date:** 2026-10-13
**Status:** PROPOSED / DRAFT. Not authorized to merge, deploy or mutate production.
**Scope:** SolidPOS offline sync operator recovery; unchanged remote sync contract `schemaVersion=4`.

## Context

The local SQLite controlled recovery operation introduced two distinct permissions:
`sync.recovery.execute` and `sync.recovery.approve`. An executor needs a valid
local session and an independent reviewer's time-bound approval. The PosServer
permission catalog and PostgreSQL `pos.permissions` originally did not know
these permissions. Furthermore, the deployed `pos.seed_mvp_roles` definition
granted **every current and future permission to `owner`**, so simply inserting
the new codes would silently grant recovery rights to future provisioned owners
or to existing owners during role reseeding.

## Proposed design

1. Register both codes in `PermissionCodes.All` for explicit administrator role
   assignment. **Do not** add them to `TerminalPermissionSet.Default`.
2. Keep the proposed SQL under `docs/engineering/proposals/pos-sync-recovery-rbac.proposed.sql`, outside the production migration chain until separately approved. Never
   modify an existing applied numbered migration. This transaction only
   registers two permission descriptions and replaces `pos.seed_mvp_roles`
   to exclude these two codes from the owner's formerly unrestricted seed rule.
   The new definition explicitly sets `search_path = pos, public` on the
   function, repairing a discovered legacy session-dependent name resolution
   issue; the original 003 script is deliberately unchanged.
   Existing `pos.role_permissions` is never changed, including no automatic
   grants to old owners/admins/managers/cashiers.
3. An authenticated, authorized central RBAC administrator must independently
   provision the two permissions on appropriate roles/users. The operator and
   reviewer are separate principals, validated again by SQLite at execution.
4. The SQLite gate uses the **real PBKDF2 local login** and cached permissions
   through `LocalAuthService`, and requires the cache to remain within the
   user's `MaxOfflineHours` authorization window on every approval and execute
   call. Valid login alone does not authorize a cached permission forever.
5. Run a disposable PostgreSQL 16 GitHub Actions service to validate the
   migration **twice**, new and existing tenant provisioning, absence of
   privilege escalation and survival of explicitly granted permission.
   The ephemeral service has no production credentials or network connection
   to deployed applications.

## Consequences / remaining design work

- This implements **central catalog + explicit role migration + real SQLite
  authentication validation**, not the online flow for securely importing fresh
  role permissions into the local POS. There is **no authenticated central →
  local cache provisioning adapter** in this increment. Never claim otherwise.
- The central role assignment APIs might allow either elevated permission to
  a single user. SQLite's separate-executor/reviewer check still blocks the
  user from self-approving. Formal administrative separation and eligibility
  policy require independent review.
- Cached offline permission revocation cannot be immediate without network
  access. The cache expiry window bounds, but does not eliminate, that risk.
  Recommend an online-only revalidation policy for future high-risk recovery,
  coordinated with the product owner.
- Replacing a previously deployed PostgreSQL function is a privileged
  migration; its execution requires schema review, a verified disposable test,
  backup/rollback plan and explicit PO approval. Running a green GitHub CI
  alone does not constitute authorization to migrate any real database.
- No automatic grant changes to historical business roles. Remote POS sync,
  inventory, sales, terminal registration, WPF hardware and Railway deployment
  behavior are unchanged.

## QA evidence

- `SQLiteAuthenticatedRecoveryFlowTests.cs`: actual local password login,
  two-user controlled recovery, cache expiry after login, revoked permissions,
  cross-store identity, role-label default deny.
- `SyncRecoveryPermissionCatalogTests.cs`: central catalog registration and
  default terminal exclusion.
- `solidpos-rbac-postgresql-ephemeral.yml`: PostgreSQL 16 ephemeral service,
  schema and original seed, additive migration repeated twice, smoke assertions.

**GO/NO-GO:** PR remains draft until tests and approval review. No production execution.

## Governance gate discovered during CI

The existing V1.1-10 static contract explicitly rejects `database/postgresql/022_*.sql` as unauthorized. The first attempt added such a numbered migration and **failed** Windows CI. We restored the invariant by removing it from `database/postgresql` and keeping the reviewed SQL only as a non-deployable proposal, used by a disposable PostgreSQL fixture. The static gate is preserved and was never bypassed. A future release requires an explicit ADR/PO decision to extend the approved migration sequence in its own controlled change.
