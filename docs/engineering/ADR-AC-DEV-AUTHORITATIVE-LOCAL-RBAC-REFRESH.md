# ADR AC-DEV — Authoritative local permission-cache refresh

**Date:** 2026-10-13. **Status:** PROPOSED / not production-ready.
**Branch:** `AC-DEV`. No merge, no deploy, no external services during development.

## Problem

The server returns effective permissions with `AuthSessionResponse.User.Permissions`,
but that login response does **not** contain an authority version, the
store/terminal-specific permission scope or an integrity proof for offline cache
updates. Treating arbitrary client-supplied JSON or role labels as proof would
silently create an authorization bypass. The remote `schemaVersion=4` sync
contract and existing `/auth/login` wire format are frozen and unchanged.

## Scoped implementation (current increment)

- `AuthoritativePermissionRefreshService` accepts ONLY an injected
  `IVerifiedPermissionSnapshotSource`. For now its sole implementation is an
  in-memory test double. A production implementation must authenticate to the
  central service, validate the authorized user and terminal, verify the
  server-issued monotonic version, and ensure the payload is genuine.
- Each snapshot must bind `TenantId`, `StoreId`, `TerminalId`, `UserId`,
  positive `AuthorityVersion`, fresh `IssuedAtUtc` (±2 minutes), `IsActive`,
  `RoleCode` and **explicit effective permission codes**. Role names are never
  permission grants; an inactive user must have zero effective grants.
- The SQLite implementation refuses snapshots without an existing local user
  and enrolled terminal with matching scope. It transactionally updates user
  status/role/cache freshness, atomically replaces that user's permission set,
  persists `local_authority_snapshot_state` (monotonic replay guard), closes
  the user's active sessions on privilege changes, and writes a local audit
  event. The original PBKDF2 password hash and offline time-window cap are
  preserved.
- A stale or duplicated version cannot resurrect revoked permissions. A
  session held before a privilege change cannot exercise newly revoked grants
  without reauthenticating. A no-change newer version keeps current sessions.
- The SQLite tests use real `LocalAuthService.LoginAsync` and disposable
  SQLite, plus a simulated trusted remote authority and deliberately injected
  audit failures. They cover scope mismatch, tampering, version replay,
  deactivation, no-change refresh and rollback.

## Non-goals / security gaps blocking production

1. **No authenticated HTTP/JWT client adapter exists yet** for
   `IVerifiedPermissionSnapshotSource`. Merely implementing that interface
   does not prove that a payload is genuine. A genuine online integration and
   explicit authority-version contract must be designed and approved first.
2. Existing `SaveLocalUserAsync` and development CLI `sync-local-user` are
   legacy provisioning paths without proof of central authority. The SQLite
   repository now **rejects all legacy updates for users who already have a
   versioned central-authority receipt**, preventing the old CLI from
   resurrecting revoked grants after enrollment. Legacy initial provisioning
   is still possible before the first verified snapshot (needed for offline
   credential enrollment and current fixtures). This bootstrap path must be
   separately secured for elevated recovery permissions in production.
   A malicious process with direct SQLite write access is still out of scope.
3. Server role store access and immediate role revocation have not yet been
   synchronized end-to-end. Time-bounded offline cache mitigates but cannot
   eliminate stale-authority risk. For high-risk recovery, require a policy
   decision on online revalidation.
4. The proposed recovery-permission SQL is under
   `docs/engineering/proposals/` and remains unapproved/non-deployable. The
   existing V1.1 migration allowlist is unchanged.
5. The PostgreSQL disposable RBAC smoke tests the *proposal only*, not full
   PosServer authentication against PostgreSQL or WPF device recovery flows.

**Next gate:** central authority snapshot version/scoping design, authenticated
endpoint and trusted client, secure deprecation of legacy elevated-grant
provisioning, disposable PostgreSQL integration test, independent security review,
Product Owner GO/NO-GO. Never interpret green CI as production approval.
