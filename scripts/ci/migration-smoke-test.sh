#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${DATABASE_URL:-}" ]]; then
  echo "DATABASE_URL is required." >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"

bash "${ROOT_DIR}/scripts/wait-for-postgres.sh"
bash "${ROOT_DIR}/scripts/apply-postgresql-migrations.sh"
psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/004_seed_dev_auth.sql"

psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 <<'SQL'
SET search_path TO pos, public;

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
      ('update_channels'),
      ('update_releases')
  ) AS required(required_table)
  WHERE to_regclass('pos.' || required_table) IS NULL;

  IF missing IS NOT NULL THEN
    RAISE EXCEPTION 'Missing required runtime tables: %', missing;
  END IF;
END $$;

SELECT id, name, status
FROM tenants
WHERE id = '11111111-1111-1111-1111-111111111111';

SELECT code
FROM update_channels
WHERE code IN ('stable', 'beta', 'internal')
ORDER BY code;
SQL

echo "Migration smoke test passed."
