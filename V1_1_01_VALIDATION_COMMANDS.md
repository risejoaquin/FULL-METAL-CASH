# V1.1-01 — Validation Commands

## CI automática — GitHub Actions
El repo incluye `.github/workflows/solidpos-ci.yml`. En cada push/PR a las ramas configuradas ejecuta secret scan, restore, build Release y `dotnet test` completo sobre `windows-latest`, y publica los `.trx` como artefacto.

El resultado verde de **SolidPOS CI / Build and test (.NET 8 / Windows)** es la evidencia primaria para restore/build/test. La validación local se reserva para pruebas que requieran el entorno operativo local, API levantada, credenciales seguras o diagnóstico de fallos.


## 1. Restore
```powershell
dotnet restore solidpos-platform.sln
```
Esperado: restore PASS.

## 2. Build
```powershell
dotnet build solidpos-platform.sln
```
Esperado: 0 errores; baseline objetivo mantiene build limpio.

## 3. Tests
```powershell
dotnet test solidpos-platform.sln
```
Esperado: todos los tests PASS. El total puede aumentar por tests nuevos de observabilidad.

## 4. Secret scan
```powershell
.\scripts\security\scan-local-secrets.ps1
```
Esperado: PASS / sin secretos.

## 5. Levantar API
```powershell
.\scripts\run-posserver-dev.ps1
```

## 6. Health + correlation IDs
```powershell
$cid = "v1101-$([Guid]::NewGuid().ToString('N'))"
$r = Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:5000/health/live" -Headers @{ "X-Correlation-Id" = $cid }
$r.StatusCode
$r.Headers["X-Correlation-Id"]
$r.Headers["X-Request-Id"]
Invoke-RestMethod "http://localhost:5000/health/ready"
```
Esperado: 200; correlation ID preservado; request ID presente; readiness `ready`.

## 7. Validator de fase
```powershell
Unblock-File .\scripts\v1.1\validate-v1.1-01-observability-foundation.ps1
.\scripts\v1.1\validate-v1.1-01-observability-foundation.ps1 -BaseUrl "http://localhost:5000" -SkipDotNet
```
Esperado: `PASS V1.1-01 OBSERVABILITY FOUNDATION`.

## 8. Métricas autenticadas
Después de login, pasa el JWT:
```powershell
.\scripts\v1.1\validate-v1.1-01-observability-foundation.ps1 `
  -BaseUrl "http://localhost:5000" `
  -BearerToken $token `
  -SkipDotNet
```
Esperado: respuesta incluye `requests`, `sync` y `database`.

## Logs a enviar si falla
Enviar el bloque completo desde el primer comando fallido, incluyendo: salida de restore/build/test, excepción PowerShell del validator, status/body de health, y logs JSON de PosServer que compartan `trace_id`, `correlation_id` o `request_id`. No enviar JWT, password ni DATABASE_URL.
