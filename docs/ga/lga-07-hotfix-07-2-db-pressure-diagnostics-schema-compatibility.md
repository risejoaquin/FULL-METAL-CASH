# LGA-07 HOTFIX 07.2 — DB Pressure Diagnostics Schema Compatibility

## Purpose

This hotfix corrects the LGA-07.1 diagnostic SQL so the db pressure snapshot is schema compatible with the production PostgreSQL diagnostic context.

## Contract

- lga-07.2
- db pressure
- waiting connections
- diagnostic only
- public ga not activated
- no baseline increase
- schema compatibility

## Decision

This hotfix does not accept 13 waiting connections as a new baseline. It only fixes the diagnostic query so LGA-07 can remain blocked for the correct operational reason instead of failing due to SQL shape.

## Public GA

public ga not activated. Public GA remains NOT_ACTIVATED.

## Scope

- Fix SQL diagnostic compatibility.
- Use the already-computed activity age for long-running query classification instead of referencing query_start outside the activity CTE.
- Keep AllowedWaitingConnectionCount at 12.
- Keep DiagnosticEscalationConnectionCount at 13.

## Expected result

If waiting connections remain at 13 or higher, the hotfix should PASS as a diagnostic package and report LGA-07 as pending with capacity upgrade or DB pool remediation required.
