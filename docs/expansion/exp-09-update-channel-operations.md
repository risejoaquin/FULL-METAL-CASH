# EXP-09 — Update Channel Operations

## Channel owners
- internal: engineering owner.
- beta: support and customer success owner.
- stable: release manager owner.

## Candidate channel
The candidate path uses the `internal` channel for controlled production validation before any stable promotion.

## Stable channel
Stable channel requires release notes, smoke test, migration preflight, rollback version, artifact hash, signature, and support readiness.

## Velopack package
The package type is `velopack`, with universal installer required for the current Windows POS packaging strategy.
