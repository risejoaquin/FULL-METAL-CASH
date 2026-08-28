# SolidPOS — Post-Public-GA Activation Validation Hotfix 01

## Cause
The post-activation validator used an abbreviated concurrency probe that could collapse failed/parse-error curl executions into `status=0` without surfacing the underlying error. This produced `successCount=0`, `failureCount=6`, `p95Ms=0` even though the immediately preceding public-GA activation postflight used the proven probe implementation successfully.

## Fix
- Replaced the post-GA concurrency probe with the exact proven implementation from Public GA Activation Execution Hotfix 01.
- Preserved curl-reported HTTP latency measurement (job startup is outside the measured latency).
- Added unique error diagnostics to the probe result when a request cannot be parsed or executed.
- Validator version: `POST-PUBLIC-GA-ACTIVATION-VALIDATION.1.1-proven-capacity-probe`.

## Scope
No database writes, no activation state changes, no threshold changes, no API contract changes.
