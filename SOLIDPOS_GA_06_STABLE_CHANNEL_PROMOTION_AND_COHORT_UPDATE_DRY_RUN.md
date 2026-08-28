# SOLIDPOS GA-06 — Stable Channel Promotion and Cohort Update Dry Run

Status: `PENDING USER VALIDATION`

## Objective
Promote the GA-05 validated RC through `internal -> beta -> stable` using exact artifact identity and prove a reduced terminal cohort update check without general rollout.

## Production safety boundary
- stable release remains `mandatory = False`;
- release remains `tenantScoped = True`;
- cohort size is exactly one controlled validation terminal;
- `targetTerminalIds` is persisted server-side;
- `GET /updates/check` requires the matching `terminalId` for targeted releases;
- a terminal outside cohort must receive `updateAvailable = False`;
- a request without terminal identity must not see a targeted stable release;
- rollback is a transaction dry run and persists no revoke;
- `generalAvailabilityActivated = False`;
- public rollout is not authorized by GA-06.

## Signing boundary
GA-05 passed with `VALIDATION_SELF_SIGNED`. GA-06 may use that proven artifact identity only for the controlled cohort dry run. `productionSigningRequiredBeforePublicPromotion = True` remains a hard condition for a public rollout.

## Database/API hardening
GA-06 adds migration `019_update_release_cohort_targeting.sql` and backward-compatible update targeting. Existing releases without targets remain tenant-wide. Releases with targets are offered only when the update check supplies an active targeted terminal.

## Required production result
`PASS GA STABLE CHANNEL PROMOTION COHORT DRY RUN / GO GA-07`

GA-07 remains locked until real production logs show the exact PASS.
