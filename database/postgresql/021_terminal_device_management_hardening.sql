BEGIN;

SET search_path TO pos, public;

-- V1.1-08: Terminal & Device Management Hardening
-- Adds nullable JSONB columns for device telemetry health and remote config metadata overrides.
-- Preserves schemaVersion = 4 and syncContract = schema_version_4.
ALTER TABLE pos.terminals
  ADD COLUMN IF NOT EXISTS device_health jsonb DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS remote_config_metadata jsonb DEFAULT NULL;

COMMIT;
