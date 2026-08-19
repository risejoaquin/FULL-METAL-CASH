#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${PRODUCTION_DATABASE_URL:-}" ]]; then
  echo "PRODUCTION_DATABASE_URL is required." >&2
  exit 1
fi

case "${PRODUCTION_DATABASE_URL}" in
  *localhost*|*127.0.0.1*|*host.docker.internal*)
    echo "Refusing to run production migrations against a local database URL." >&2
    exit 1
    ;;
esac

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"

export DATABASE_URL="${PRODUCTION_DATABASE_URL}"

schema_exists="$(psql "${DATABASE_URL}" -tAc "select case when to_regnamespace('pos') is null then 'false' else 'true' end;")"

if [[ "${schema_exists}" == "false" ]]; then
  echo "Schema pos does not exist. Applying full PostgreSQL migration chain."
  bash "${ROOT_DIR}/scripts/apply-postgresql-migrations.sh"
else
  echo "Schema pos already exists. Applying idempotent runtime migrations and validating runtime schema."
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/002_seed_permissions.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/003_seed_mvp_defaults.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/005_sync_push_runtime.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/006_sync_processing_runtime.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/007_modifier_inventory_semantics.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/008_digital_receipts_runtime.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/009_returns_refunds_runtime.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/010_customers_runtime.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/011_discounts_promotions_runtime.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/012_inventory_control_hardening.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/013_sync_conflict_resolution_runtime.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/014_builder_updates_runtime.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/015_security_auth_hardening.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/016_production_provisioning_bootstrap.sql"
  psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/017_pos_operational_completion.sql"
psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/018_sync_e2e_contract_hardening.sql"
fi

psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 <<'SQL'
DO $$
DECLARE
  missing text[];
BEGIN
  SELECT array_agg(required_table)
  INTO missing
  FROM (
    VALUES
      ('tenants'),
      ('stores'),
      ('users'),
      ('roles'),
      ('permissions'),
      ('sales'),
      ('payments'),
      ('inventory_ledger'),
      ('sync_inbox_events'),
      ('sync_conflicts'),
      ('audit_events'),
      ('builder_projects'),
      ('builder_builds'),
      ('update_releases'),
      ('production_bootstrap_runs'),
      ('cash_movements')
  ) AS required(required_table)
  WHERE to_regclass('pos.' || required_table) IS NULL;

  IF missing IS NOT NULL THEN
    RAISE EXCEPTION 'Missing required production runtime tables: %', missing;
  END IF;
END $$;
SQL

echo "Production PostgreSQL migration validation passed."
