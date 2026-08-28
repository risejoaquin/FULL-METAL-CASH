# SolidPOS PILOT-05 HOTFIX 05.1 — Offline validator PowerShell here-string parse fix

## Estado

`PENDING USER VALIDATION`

## Motivo

El validador de PILOT-05 falló antes de ejecutar la prueba real por error de parseo PowerShell:

```text
Falta la cadena en el terminador: "@.
```

## Causa

El bloque final de generación del log usaba un here-string PowerShell. En el entorno del usuario el parser lo interpretó como no cerrado y detuvo el script en tiempo de parseo.

## Cambio aplicado

Se reemplazó el here-string por un arreglo explícito `$logLines` y `Set-Content`, evitando terminadores `@"` / `"@`.

Archivo modificado:

```text
scripts/pilot/validate-offline-mode-field-test.ps1
```

## Módulos afectados

```text
scripts/pilot
```

## Decisión técnica

Eliminar el uso de here-strings en el validador piloto para reducir riesgo de falsos FAIL por parsing de PowerShell, codificación o copias con espacios invisibles.

## Riesgos

- Este hotfix solo corrige el parseo inicial del script.
- PILOT-05 sigue en `PENDING USER VALIDATION` hasta ejecutar nuevamente el validador completo contra producción/Supabase.
- No se ejecutó build/test local en este entorno porque no hay SDK .NET disponible.

## Comando de validación

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)

.\scripts\pilot\validate-offline-mode-field-test.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -StoreCode "MAIN" `
  -ProductSku "QSR-AMERICANO"
```

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
