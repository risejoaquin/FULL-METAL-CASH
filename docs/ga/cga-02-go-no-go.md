# CGA-02 Go / No-Go

## PASS

CGA-02 may PASS when:

- API samples pass
- DB monitoring snapshot passes
- sync pending/retry/conflicts remain 0
- duplicateLocalSaleCount remains 0
- staleProcessingCount remains 0
- rlsMissingTableCount remains 0
- longRunningQueryCount remains 0
- Public GA: NOT ACTIVATED

## BLOCKED

CGA-02 is BLOCKED if:

- dashboard overview fails with the correct contract
- health/ready fails
- observability is public without auth
- sync contract drifts from schemaVersion 4
- pending conflicts appear
- retry pending events appear
- stale processing appears
- duplicate sales appear
- RLS drift appears
- Public GA is activated without explicit decision

## NO-GO

NO-GO means do not continue to CGA-03 until remediation or formal acceptance is complete.

## Decision output

PASS output:

`PASS CGA-02 PRODUCTION MONITORING INCIDENT WINDOW / GO CGA-03`

PUBLIC GA: NOT ACTIVATED


## CGA-02.1 known conflict baseline

When CGA-02 is rerun after a documented operator test created known historical sync conflicts before the valid monitoring activity, use `-AllowedExistingSyncConflictCount <count>` to prevent the baseline from masking new blockers. The count must match the known existing conflicts and remains a carried condition, not a public GA approval.
