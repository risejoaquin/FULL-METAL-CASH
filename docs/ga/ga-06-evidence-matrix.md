# GA-06 Evidence Matrix

| Gate | Required evidence |
|---|---|
| GA-05 RC | PASS REAL PRODUCTION baseline |
| Promotion | internal -> beta -> stable |
| Artifact identity | same URL/hash/signature across all 3 |
| Stable state | activeStableReleaseCount >= 1 |
| Safety | mandatory=False, tenantScoped=True |
| Cohort | exactly 1 controlled target terminal |
| Positive update | updateAvailable=True for target |
| Negative update | False outside cohort and without terminalId |
| Compatibility | target active with appVersion |
| Rollback | beta rollback target exists + transaction drill GO |
| Audit | releaseAuditCount >= 1 and cohort audit evidence |
| Contract | schemaVersion=4, syncContract=schema_version_4 |
| GA | generalAvailabilityActivated=False |
