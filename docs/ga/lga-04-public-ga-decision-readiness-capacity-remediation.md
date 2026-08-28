# LGA-04 — Limited GA Public GA Decision Readiness or Capacity Remediation

## Objective
LGA-04 validates whether the platform is ready for a public GA decision package or must remain in Limited GA while capacity remediation is completed.

## Mandatory posture
- Public GA not activated.
- Public GA decision only; no automatic activation.
- LGA-03 multi-day burn-in must already be closed as PASS.
- Capacity boundary must be remediated or formally accepted for limited capacity.
- schemaVersion 4 and syncContract schema_version_4 remain mandatory.

## Gate interpretation
- PASS / KEEP LIMITED GA - CAPACITY REMEDIATION REQUIRED means operational posture is stable, but broad Public GA is not ready.
- PASS / READY FOR PUBLIC GA DECISION PACKAGE means the decision package can be prepared, still without automatic activation.

## Known carried-forward risk
The GA-09 capacity boundary found that concurrency above the limited scope can produce upstream errors on Railway. LGA-04 keeps this as a formal decision item instead of silently activating Public GA.
