# CGA-03 GO / NO-GO

## GO CGA-04

GO CGA-04 is allowed only when:

- CGA-02 entry gate is satisfied.
- Capacity/DB decision mode is explicit.
- Blockers are empty.
- Public GA is NOT ACTIVATED.
- Limited scope remains within max stores and max concurrent terminals.
- Known baselines do not increase beyond the allowed thresholds.

## NO_GO

NO_GO is required when any of the following occurs:

- Public GA is already activated before CGA-04.
- active stores exceed the accepted limited rollout scope.
- open shifts exceed max concurrent terminal acceptance.
- sync conflicts exceed allowed baseline.
- dead letters exceed allowed baseline.
- waiting DB connections exceed allowed baseline.
- retry/pending/stale processing events exist.
- RLS drift is detected.
- duplicate local sales are detected.
- capacity probes fail or p95 exceeds threshold.

## Public GA

CGA-03 does not activate Public GA. Public GA requires a separate CGA-04 activation decision.
