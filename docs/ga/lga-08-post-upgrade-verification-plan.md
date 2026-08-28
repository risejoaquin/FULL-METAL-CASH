# LGA-08 — Post-Upgrade Verification Plan

## Purpose

This plan is used only when an infrastructure capacity change has already been made outside SolidPOS. It performs post-upgrade verification while the product remains in Limited GA and Public GA not activated.

## Preconditions

1. LGA-07 is closed as PASS.
2. The infrastructure change is documented externally.
3. Rollback remains available.
4. No validator flag or application setting has activated Public GA.

## Capacity probe

The validator probes `/health/live` and `/health/ready` with the configured concurrency and request count. `POST_UPGRADE_VERIFICATION` requires:

- no failed requests;
- live p95 <= `MaxReadinessP95Ms`;
- ready p95 <= `MaxReadinessP95Ms`.

The default public-readiness probe threshold remains concurrency 3, 6 requests, p95 <= 1200 ms. Passing this probe verifies the upgrade only; it does not authorize Public GA.

## Safety gates

The same operational gates remain mandatory after upgrade: negative stock 0, waiting connections <= 12, sync conflict <= 3, dead letter <= 1, no stale processing, no retry pending, no duplicate local sales, no negative payments, RLS intact and schema version 4.

## Failure / rollback

If the capacity probe or any operational invariant fails, LGA-08 must fail. Do not increase baselines to force PASS. Roll back or remediate the external infrastructure change, then re-run LGA-08. Public GA remains not activated throughout rollback and remediation.
