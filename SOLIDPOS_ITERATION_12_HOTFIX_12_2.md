# SolidPOS Iteration 12 Hotfix 12.2 — Expected Offline Window Failure Capture

## Problema

El script `scripts/poscore/validate-poscore-offline-auth-rbac.ps1` validaba correctamente que un usuario con cache offline vencido fallara, pero PowerShell detenía la ejecución antes de que el script pudiera inspeccionar el mensaje esperado.

Error observado:

```text
Offline login blocked because local auth cache is older than the allowed offline window.
```

Ese error es el comportamiento correcto del runtime, pero el script lo trataba como fallo terminal.

## Corrección

Se ajustó únicamente el bloque de validación del usuario expirado para capturar stdout/stderr y `$LASTEXITCODE` con `$ErrorActionPreference = "Continue"` temporalmente. Después restaura el valor original.

## Resultado esperado

El script debe imprimir:

```text
Expired offline user blocked correctly.
```

y continuar con venta offline autenticada, sync, recibo, cierre de caja y logout local.

## Archivos modificados

```text
scripts/poscore/validate-poscore-offline-auth-rbac.ps1
SOLIDPOS_ITERATION_12_HOTFIX_12_2.md
```
