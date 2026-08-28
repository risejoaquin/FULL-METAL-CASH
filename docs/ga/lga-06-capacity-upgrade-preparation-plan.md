# LGA-06 Capacity Upgrade Preparation Plan

## Purpose

Document capacity upgrade preparation while continuing Limited GA with formal capacity risk accepted.

## Current Position

Capacity remediation is deferred until Railway Pro or an equivalent scaling upgrade. Public GA remains NOT_ACTIVATED.

## Railway Pro Preparation

Before upgrading capacity:

1. Confirm the production backend/API service in Railway.
2. Confirm Dashboard is not the target of the backend capacity remediation.
3. Prepare same-region replicas or plan upgrade path.
4. Keep a rollback plan to return to the previous service configuration.
5. Re-run LGA-04 or later capacity verification after the upgrade.

## Rollback

If the Railway Pro / capacity upgrade causes instability:

- Roll back to the previous Railway deployment configuration.
- Keep Public GA off.
- Keep Limited GA active only if the LGA continuation gates stay clean.

## Non-goals

- No Public GA activation.
- No threshold lowering.
- No bypass of capacity remediation.
