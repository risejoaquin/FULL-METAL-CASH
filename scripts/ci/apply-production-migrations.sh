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
  echo "Schema pos already exists. Skipping non-idempotent baseline migration and validating runtime schema."
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
      ('update_releases')
  ) AS required(required_table)
  WHERE to_regclass('pos.' || required_table) IS NULL;

  IF missing IS NOT NULL THEN
    RAISE EXCEPTION 'Missing required production runtime tables: %', missing;
  END IF;
END $$;
SQL

echo "Production PostgreSQL migration validation passed."
