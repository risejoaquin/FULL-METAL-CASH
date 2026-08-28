# SolidPOS GA-10 Hotfix 10.1 — PowerShell Join-Path Array Guardrail

Fecha: 2026-08-23.

## Estado

GA-10.0 falló antes de ejecutar build/test por un defecto del validator PowerShell.

## Evidencia

PowerShell reportó:

```text
No se puede convertir 'System.Object[]' al tipo 'System.String' requerido por el parámetro 'ChildPath'.
```

## Causa

El validator GA-10.0 construía el arreglo `$docs` con llamadas `Join-Path` sin paréntesis dentro de un array literal. PowerShell interpretó las comas del array como parte de los argumentos del comando, generando un `System.Object[]` para `-ChildPath`.

Además, la creación de directorios usaba argumentos posicionales múltiples en `New-Item`; se cambió a `-Path @(...)` explícito.

## Corrección

- Version bump: `GA-10.1-powershell-joinpath-array-guardrail`.
- Parentizar cada `Join-Path` dentro de `$docs`.
- Usar `New-Item -Path @($runtime, (Split-Path $logPath))`.

## Alcance

Solo cambia el validator GA-10.

No cambia:

- PosServer backend.
- PosDashboard.
- PosCore.
- PosBuilder.
- Migraciones DB.
- Contratos C#.
- schemaVersion.
- syncContract.

## Decisión técnica

La falla es de validator/script. No hay evidencia de defecto en backend, Railway, DB, auth, RLS, dashboard ni observability runtime.
