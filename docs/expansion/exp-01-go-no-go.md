# EXP-01 GO/NO-GO

Status: PENDING USER VALIDATION

## GO criteria

EXP-01 can close as GO when all are true:

- dotnet restore passes.
- dotnet build passes.
- dotnet test passes.
- secret scan passes.
- pilot evidence documents exist for PILOT-01 to PILOT-10.
- release docs exist.
- production health/live is alive.
- production health/ready is ready.
- admin login succeeds.
- protected metrics are readable.
- baseline manifest is generated.

## NO-GO criteria

EXP-01 is NO-GO if any of the following occurs:

- build fails.
- tests fail.
- secret scan fails.
- pilot evidence is missing.
- release baseline docs are missing.
- production readiness fails.
- admin login fails.
- protected metrics fail.

## Conditions for EXP-02

EXP-02 may start after EXP-01 PASS, but the expansion must keep these conditions visible:

- monitor retry_pending sync item.
- triage known dead_letter sync item.
- perform inventory reconciliation for negative inventory item.

## Decision

```text
EXP-01 PASS = GO EXP-02
```
