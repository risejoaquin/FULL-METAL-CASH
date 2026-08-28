# SolidPOS LGA-04 HOTFIX 04.3 — Formal Limited Capacity Acceptance Contract Alignment

## Contexto
LGA-04.2 aceptaba `FORMAL_ACCEPT_LIMITED_CAPACITY` como parámetro, pero el blocker matrix todavía lo trataba como falta de decisión de remediación cuando el capacity probe fallaba.

## Cambio
El blocker `capacity_boundary_failed_without_remediation_decision` ahora considera válidas ambas decisiones explícitas:

- `REMEDIATE_BEFORE_PUBLIC_GA`
- `FORMAL_ACCEPT_LIMITED_CAPACITY`

Cuando `capacityProbePassed = false`, `CapacityDecision = FORMAL_ACCEPT_LIMITED_CAPACITY` y `PublicGaDecision = KEEP_LIMITED_GA`, el estado final queda:

```text
PASS LGA-04 LIMITED GA PUBLIC GA DECISION READINESS / KEEP LIMITED GA - FORMAL LIMITED CAPACITY ACCEPTED
```

## Invariantes
- No activa Public GA.
- No cambia umbrales de capacidad.
- No oculta `capacityProbePassed = false`.
- Documenta el riesgo como aceptado solo para Limited GA.

## Riesgo aceptado
La capacidad actual queda aceptada temporalmente hasta subir Railway Pro o aumentar capacidad equivalente.
