#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${DATABASE_URL:-}" ]]; then
  echo "DATABASE_URL is required." >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -f "${ROOT_DIR}/database/postgresql/001_initial_schema_postgresql.sql"
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

echo "PostgreSQL migrations applied."
