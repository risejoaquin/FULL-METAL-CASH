# SolidPOS — LGA-09 Limited GA Stability Confirmation / Capacity Risk Review

LGA-09 confirms accumulated production stability after LGA-08 and performs the formal capacity-risk review required by the Limited GA roadmap.

## Decision model

- `CONTINUE_LIMITED_GA`: allowed when stability conditions pass and may be used when the capacity boundary is healthy.
- `CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA`: required when the concurrency-3 capacity probe remains outside threshold while Limited GA remains stable.

## Safety boundary

This package cannot activate Public GA. `PublicGaDecision` accepts only `KEEP_LIMITED_GA`, database GA flags must remain false, schema version 4 is mandatory, negative stock cannot exceed zero, and waiting-connections baseline cannot exceed 12.

## Expected current path

Given the last LGA-08 evidence, run LGA-09 with `CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA` unless infrastructure has actually been upgraded and the concurrency-3 probe now passes.
