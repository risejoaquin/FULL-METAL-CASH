# SolidPOS GA-08 Hotfix 08.11 — PowerShell Assertion Type Safety

## Motivo
GA-08.10 confirmó que el bloque de autenticación ya pasa completo, incluyendo refresh rotation, reuse rejection y logout. El siguiente fallo ocurrió en el validator local al entrar al gate de aislamiento cross-tenant:

```text
No se puede procesar la transformación del argumento del parámetro c.
No se puede convertir el valor "System.String" al tipo "System.Boolean".
```

## Causa
El helper local `Assert-True([bool]$c, [string]$m)` obligaba a PowerShell a convertir strings a booleanos. En el gate cross-tenant, `$sql.foreignTenantId` es un string GUID válido, pero PowerShell no lo transforma automáticamente en `[bool]`, provocando un error del validator antes de terminar GA-08.

## Corrección
- `Assert-True` ahora acepta valores booleanos, strings, números, arrays y nulls de forma explícita.
- El check de `foreignTenantId` ahora valida string no vacío con `IsNullOrWhiteSpace`.
- No cambia backend, base de datos, RLS, refresh rotation ni contratos de API.

## Riesgo
Bajo. Es una corrección del validator local para evitar fallos por tipado de PowerShell.

## Estado esperado
GA-08 debe continuar después de:

```text
[GA-08] Cross-tenant API isolation negative reads...
```

y cerrar con:

```text
[GA-08] GA-08 PASS GA SECURITY TENANT ISOLATION ACCESS CONTROL / GO GA-09
```
