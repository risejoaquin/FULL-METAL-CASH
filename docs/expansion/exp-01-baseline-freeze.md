# EXP-01 Post-Pilot Baseline Freeze

Status: PENDING USER VALIDATION
Recommended tag: v0.10.0-post-pilot.20260820

## Baseline statement

SolidPOS has completed the controlled production pilot. PILOT-01 through PILOT-10 are treated as PASS REAL PRODUCTION / GO based on the production validation logs provided during the pilot cycle.

## Production decision inherited

```text
Production Expansion Decision = GO_LIMITED_EXPANSION
```

This baseline does not authorize a mass rollout. It freezes the current known-good state so EXP-02 can convert it into a repeatable expansion readiness pack.

## Baseline contents

- Source code after PILOT-10 HOTFIX 10.4.
- Pilot evidence documents from PILOT-01 to PILOT-10.
- Pilot validation commands from PILOT-01 to PILOT-10.
- Pilot hotfix evidence through HOTFIX 10.4.
- Post-pilot roadmap.
- Release documentation.
- EXP-01 validator.

## Required validation

- dotnet restore.
- dotnet build.
- dotnet test.
- secret scan.
- dashboard build when not skipped.
- pilot evidence contract.
- release docs contract.
- production health/readiness.
- admin login.
- protected observability metrics.

## GO/NO-GO

GO to EXP-02 only after the EXP-01 validator returns PASS.
