# SolidPOS PILOT-05 HOTFIX 05.2 - PowerShell ASCII/BOM parser hardening

Estado: PENDING USER VALIDATION

## Motivo

El HOTFIX 05.1 elimino el here-string, pero el validador aun contenia un guion largo Unicode en el bloque de log. En Windows PowerShell 5.1, un archivo UTF-8 sin BOM puede ser interpretado con codepage ANSI y convertir ese caracter en mojibake, rompiendo el parseo del arreglo `$logLines`.

## Cambio

- `scripts/pilot/validate-offline-mode-field-test.ps1` queda ASCII-safe en el bloque de log.
- El archivo se guarda como UTF-8 con BOM para compatibilidad con Windows PowerShell 5.1.
- No se cambia el contrato funcional de PILOT-05.

## Validacion local disponible

No se ejecuto `dotnet build` ni `dotnet test` en este entorno porque no hay .NET SDK disponible.

Validacion estatica realizada en el artefacto: no quedan caracteres no ASCII en el script PowerShell piloto.

## Resultado esperado

```text
[PILOT-05] PILOT-05 PASS REAL PRODUCTION / GO
```

## Logs si falla

Enviar:

```text
Salida completa de PowerShell desde el primer [PILOT-05]
docs/pilot/logs/pilot-05-offline-mode-field-test-log.md
.runtime/pilot-05-offline-mode-field-test.sqlite
.runtime/pilot-05-offline-mode-field-test.sqlite-wal si existe
.runtime/pilot-05-offline-mode-field-test.sqlite-shm si existe
```
