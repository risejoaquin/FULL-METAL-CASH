-- Macro Phase 35: Security / Secrets / Production Auth Hardening.

BEGIN;

SET search_path TO pos, public;

ALTER TABLE users
  ADD COLUMN IF NOT EXISTS login_failed_count integer NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS login_last_failed_at timestamptz,
  ADD COLUMN IF NOT EXISTS locked_until timestamptz,
  ADD COLUMN IF NOT EXISTS password_changed_at timestamptz NOT NULL DEFAULT now(),
  ADD COLUMN IF NOT EXISTS password_reset_required boolean NOT NULL DEFAULT false;

CREATE INDEX IF NOT EXISTS idx_users_lockout
  ON users (tenant_id, locked_until)
  WHERE locked_until IS NOT NULL AND deleted_at IS NULL;

ALTER TABLE refresh_tokens
  ADD COLUMN IF NOT EXISTS rotated_at timestamptz,
  ADD COLUMN IF NOT EXISTS last_used_at timestamptz,
  ADD COLUMN IF NOT EXISTS revoked_reason text;

CREATE INDEX IF NOT EXISTS idx_refresh_tokens_active_hash
  ON refresh_tokens (token_hash)
  WHERE revoked_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user_active
  ON refresh_tokens (tenant_id, user_id, expires_at)
  WHERE revoked_at IS NULL;

COMMIT;
