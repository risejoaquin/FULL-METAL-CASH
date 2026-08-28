#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${DATABASE_URL:-}" ]]; then
  echo "DATABASE_URL is required." >&2
  exit 1
fi

ATTEMPTS="${POSTGRES_WAIT_ATTEMPTS:-60}"
SLEEP_SECONDS="${POSTGRES_WAIT_SLEEP_SECONDS:-2}"

for attempt in $(seq 1 "${ATTEMPTS}"); do
  if psql "${DATABASE_URL}" -v ON_ERROR_STOP=1 -tAc "SELECT 1" >/dev/null 2>&1; then
    echo "PostgreSQL is ready."
    exit 0
  fi

  echo "Waiting for PostgreSQL (${attempt}/${ATTEMPTS})..."
  sleep "${SLEEP_SECONDS}"
done

echo "PostgreSQL did not become ready in time." >&2
exit 1
