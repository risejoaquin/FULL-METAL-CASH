# LGA-01 — Limited GA Operations Hardening

## Objective

Harden Limited GA operations after CGA-04 while keeping Public GA not activated.

## Scope

LGA-01 covers:

1. Resolve or formally archive the known sync conflicts baseline.
2. Resolve or formally archive the known dead letter baseline.
3. Observe or adjust negative stock for ING-CAFE-G.
4. Correct WPF QSR command enablement for cash payment, fake receipt queue and visual sync actions.
5. Carry forward the optional capacity remediation item for Concurrency 3+.
6. Keep Public GA not activated unless an explicit separate decision is made.

## Decision

Default decision: FORMAL_ARCHIVE for sync conflicts and dead letter, OBSERVE for inventory unless `-ApplyInventoryAdjustment` is used.

## Public GA

Public GA remains NOT_ACTIVATED. LGA-01 does not activate general availability or public general availability flags.

## Evidence

The validator writes a manifest under `.runtime/lga-01-limited-ga-operations-hardening`.
