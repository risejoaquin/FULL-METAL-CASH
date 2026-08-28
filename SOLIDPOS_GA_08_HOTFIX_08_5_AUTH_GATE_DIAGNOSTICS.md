# SOLIDPOS GA-08 HOTFIX 08.5 — Auth Gate Diagnostics

## Objetivo

Corregir el validator GA-08 para aislar exactamente qué subpaso del gate de autenticación falla cuando se recibe un 401 dentro del bloque:

- login
- JWT claims
- refresh rotation
- old refresh reuse negative check
- logout
- logged-out refresh negative check

## Decisión técnica

No se modifica PosServer, base de datos, RLS, JWT, CORS ni Swagger. El login manual ya validó que credenciales/tenant funcionan. El hotfix solo mejora el contrato de evidencia del script para que un 401 no quede opaco en `Invoke-RestMethod` línea 15.

## Cambios

- `scripts/ga/validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1`
  - Agrega mensajes `Authentication subcheck ... PASS`.
  - Si falla, emite: `GA-08 authentication subcheck failed at [<stage>]`.

## Alcance

- No activa General Availability.
- No cambia `schemaVersion=4`.
- No cambia `syncContract=schema_version_4`.
- No toca producción.
