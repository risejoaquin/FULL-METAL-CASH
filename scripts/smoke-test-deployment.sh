#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-${BASE_URL:-}}"
if [[ -z "${BASE_URL}" ]]; then
  echo "Usage: scripts/smoke-test-deployment.sh https://your-service.example" >&2
  exit 1
fi
BASE_URL="${BASE_URL%/}"

EMAIL="${SMOKE_EMAIL:-owner@solidpos.local}"
PASSWORD="${SMOKE_PASSWORD:-Admin123!}"
TENANT_ID="${SMOKE_TENANT_ID:-11111111-1111-1111-1111-111111111111}"

echo "Checking liveness..."
curl --fail --silent --show-error "${BASE_URL}/health/live" | tee /tmp/solidpos_live.json >/dev/null
python3 - <<'PY'
import json
with open('/tmp/solidpos_live.json', 'r', encoding='utf-8') as f:
    data=json.load(f)
assert data.get('status') == 'alive', data
PY

echo "Checking readiness..."
curl --fail --silent --show-error "${BASE_URL}/health/ready" | tee /tmp/solidpos_ready.json >/dev/null
python3 - <<'PY'
import json
with open('/tmp/solidpos_ready.json', 'r', encoding='utf-8') as f:
    data=json.load(f)
assert data.get('status') == 'ready', data
assert data.get('database') == 'ready', data
PY

echo "Checking authenticated metrics..."
cat > /tmp/solidpos_login.json <<JSON
{"email":"${EMAIL}","password":"${PASSWORD}","tenantId":"${TENANT_ID}"}
JSON
curl --fail --silent --show-error \
  -H 'Content-Type: application/json' \
  -d @/tmp/solidpos_login.json \
  "${BASE_URL}/api/v1/auth/login" | tee /tmp/solidpos_session.json >/dev/null

TOKEN=$(python3 - <<'PY'
import json
with open('/tmp/solidpos_session.json', 'r', encoding='utf-8') as f:
    print(json.load(f).get('accessToken',''))
PY
)

if [[ -z "${TOKEN}" ]]; then
  echo "Login did not return accessToken." >&2
  exit 1
fi

curl --fail --silent --show-error \
  -H "Authorization: Bearer ${TOKEN}" \
  "${BASE_URL}/api/v1/observability/metrics" | tee /tmp/solidpos_metrics.json >/dev/null
python3 - <<'PY'
import json
with open('/tmp/solidpos_metrics.json', 'r', encoding='utf-8') as f:
    data=json.load(f)
assert data.get('database', {}).get('ready') is True, data
assert data.get('database', {}).get('requiredTablesPresent') is True, data
PY

echo "Deployment smoke test passed for ${BASE_URL}."
