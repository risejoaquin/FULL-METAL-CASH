# CGA-03 Evidence Matrix

| Evidence area | Required evidence | Gate |
|---|---|---|
| Entry gate | CGA-02 PASS logs or runtime manifest | Required |
| Build/test | dotnet restore/build/test | Required |
| Secrets | local secret scan PASS | Required |
| Health | /health/live and /health/ready 2xx | Required |
| Observability | unauthenticated 401, authenticated database.ready true | Required |
| Sync | pending=0, processing=0, retry=0 | Required |
| Sync baseline | conflictCount <= allowed baseline | Formal acceptance only |
| Dead letter baseline | deadLetterCount <= allowed baseline | Formal acceptance only |
| DB pressure | waitingConnectionCount <= allowed baseline | Formal acceptance only |
| RLS | rlsMissingTableCount = 0 | Required |
| Financial integrity | duplicateLocalSaleCount = 0, negativePaymentCount = 0 | Required |
| Capacity probes | p95 below configured threshold and no request errors | Required |
| Public GA | NOT ACTIVATED | Required |


- CGA-03.1 evidence includes `observability_p95LatencyMs_missing_fallback_to_capacity_probe_p95` only when the observability payload omits a top-level `p95LatencyMs`; the capacity decision remains based on explicit endpoint probes and DB pressure snapshot.
