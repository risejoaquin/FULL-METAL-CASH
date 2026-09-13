# SolidPOS V1.1-08: Terminal & Device Management Hardening

## Overview
Phase V1.1-08 hardens the terminal and device management lifecycle across SolidPOS PosServer, providing authoritative security controls, device telemetry, and configuration management while strictly preserving tenant and store isolation.

## Architectural Invariants
- `schemaVersion: 4` remains authoritative.
- `syncContract: schema_version_4` remains unchanged.
- Public GA behavior and backward compatibility are strictly preserved.
- Tenant and store isolation enforced authoritatively via PostgreSQL RLS and API middleware.
- Operational invariant: a revoked terminal cannot operate improperly at the authoritative boundary.
- Offline-first architecture supported with deterministic sync events and offline security boundaries.

## Implemented Scope (Exact)

### 1. Terminal Status
- Added `GET /api/v1/terminals/{terminalId}` to retrieve full terminal details including identity, assignment, operational status, version, and latest telemetry.
- Supported operational statuses: `active`, `blocked`, `pending`, `retired`.
- Status responses expose lock state: `hardLockedAt`, `hardLockReason`.

### 2. Last-Seen
- Deterministic, authoritative UTC timestamp (`last_seen_at`) tracked in PostgreSQL `pos.terminals`.
- Authoritatively updated on heartbeat reception (`POST /api/v1/terminal/heartbeat`) and terminal enrollment/token refreshes.
- Returned deterministically in ISO-8601 UTC format.

### 3. Application/Version Information
- Canonical application version (`app_version`) tracked and updated on terminal heartbeat and enrollment.
- Returned in terminal status queries and heartbeat responses to ensure fleet observability.

### 4. Store Assignment
- Added `POST /api/v1/terminals/{terminalId}/store` (`AssignTerminalStoreRequest`).
- Strict tenant/store isolation: Target store existence is validated against the authenticated tenant (`pos.stores WHERE tenant_id = @tenant_id AND id = @store_id`). Cross-tenant assignment is authoritatively rejected (HTTP 400).
- Upon reassignment, emits authoritative sync change `terminal.updated` to notify connected and syncing nodes.

### 5. Revoke/Disable Lifecycle
- Authoritative operational boundary enforcement: `TerminalValidationMiddleware` checks `IsTerminalActiveAsync` on every authenticated request with terminal context.
- **Revoke**: `POST /api/v1/terminals/{terminalId}/revoke` marks status `blocked`, reason `revoked`, clears device token hash, and emits sync change. Revocation is permanent and cannot be undone without fresh re-enrollment.
- **Disable**: `POST /api/v1/terminals/{terminalId}/disable` marks status `blocked`, reason `disabled`, and emits sync change.
- **Enable**: `POST /api/v1/terminals/{terminalId}/enable` restores disabled terminal to `active`, but strictly rejects re-enabling permanently revoked terminals.
- **Enforcement**: Any revoked or disabled terminal attempting operational endpoints (sync pull, sync push, sales, heartbeat) is rejected with HTTP 401 Unauthorized (`https://solidpos.local/problems/inactive-terminal`).

### 6. Device Health
- Added `TerminalDeviceHealthDto` telemetry model: battery status/percentage, available/total disk space, memory usage, CPU architecture, OS version, storage health flag, and report timestamp.
- Reported via `POST /api/v1/terminal/heartbeat` and stored as JSONB in `pos.terminals.device_health`.
- Queried via `GET /api/v1/terminals/{terminalId}/health` and embedded in `TerminalDetailResponse`.

### 7. Remote Config Metadata
- Added `TerminalRemoteConfigMetadata`: heartbeat interval, diagnostics enabled, log level, sync polling interval, offline grace period minutes, and update timestamp.
- Metadata only: Strictly no arbitrary remote code execution or unauthorized production behavior mutation.
- Configurable per terminal via `PUT /api/v1/terminals/{terminalId}/remote-config`.
- Delivered to terminals on every heartbeat response and retrievable via `GET /api/v1/terminal/remote-config`.

## Offline Security Boundary
- Fully offline terminals operate with local SQLite storage and cached credentials under their existing offline lease / offline grace period.
- Revocation or disablement enacted on the server cannot be known by a disconnected terminal until network communication is restored (heartbeat or sync) or local offline lease expires.
- Once connectivity is established, `TerminalValidationMiddleware` immediately rejects revoked/disabled terminals with HTTP 401, preventing any unauthorized server sync or central record mutations.

## Database Changes
- Migration: `database/postgresql/021_terminal_device_management_hardening.sql`
- Additive and non-breaking:
  - `ALTER TABLE pos.terminals ADD COLUMN IF NOT EXISTS device_health jsonb DEFAULT NULL;`
  - `ALTER TABLE pos.terminals ADD COLUMN IF NOT EXISTS remote_config_metadata jsonb DEFAULT NULL;`
- Row-level security (RLS) policies on `pos.terminals` remain fully enforced.
