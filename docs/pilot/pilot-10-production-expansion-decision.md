# SolidPOS Production Expansion Decision

## Decision

GO for limited production expansion after PILOT-10 validation passes.

This document explicitly authorizes the operator to expand only under the controls below.

## Expansion type

Controlled expansion, not full scale rollout.

## Conditions

Expansion is allowed only when health and readiness remain green, pending conflicts remain zero, failed payments in the last 24 hours remain zero, runbook and rollback plan remain current, dashboard monitoring is reviewed daily, negative inventory is reconciled before broad multi-store expansion, and known dead letter or retry pending records are explained or triaged.

## Rollback rule

Rollback is required when a SEV1 condition appears, when data integrity is uncertain, or when cash/payment reconciliation cannot be trusted.

## Monitoring rule

Monitoring must use the operations dashboard and the validated endpoints from PILOT-07 and PILOT-09.

## GO

GO means limited expansion may continue with operator oversight.

## NO-GO

NO-GO means stop expansion, preserve evidence, open incident, execute containment, and apply rollback if needed.
