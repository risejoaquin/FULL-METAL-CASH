# BETA-06 Release Promotion and Rollback Drill

BETA-06 promotes the exact release artifact from `internal` to `beta` using the deployed release contract. Promotion must preserve version, artifact URL, artifact hash, signature, package type, rollback version and tenant scope. Beta is always non-mandatory in this drill.

Rollback is validated without destructive production state: inside a PostgreSQL transaction the promoted beta release receives `revoked_at`, the state is verified, then `ROLLBACK` restores it. A post-drill update check must still resolve the promoted release.

Expected decision: `PASS BETA RELEASE PROMOTION ROLLBACK DRILL / GO BETA-07`.
