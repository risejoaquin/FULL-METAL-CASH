# CGA-01 Go / No-Go

## PASS

CGA-01 may pass only when the validator reports:

`PASS CGA-01 CONTROLLED GA ROLLOUT EXECUTION / GO CGA-02`

## BLOCKED / NO-GO

CGA-01 is `BLOCKED` / `NO-GO` if any blocker appears:

- public GA activated without explicit decision
- store or terminal scope exceeded
- duplicate local sales
- pending sync conflicts
- retry pending sync events
- stale processing events
- legacy schema events
- RLS drift
- long running DB queries
- dashboard overview failure with the correct contract

PUBLIC GA: NOT ACTIVATED remains mandatory after CGA-01.
