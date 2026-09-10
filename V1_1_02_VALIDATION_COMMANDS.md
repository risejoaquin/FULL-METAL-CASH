# V1.1-02 — Validation Commands

## Validación automatizada principal

GitHub Actions `SolidPOS CI` ejecuta:

```powershell
dotnet restore solidpos-platform.sln
```

```powershell
dotnet build solidpos-platform.sln --configuration Release --no-restore
```

```powershell
dotnet test solidpos-platform.sln --configuration Release --no-build
```

También ejecuta:

```powershell
.\scripts\security\scan-local-secrets.ps1 -Root .
```

```powershell
.\scripts\v1.1\validate-v1.1-02-production-metrics-alerting.ps1 -StaticOnly
```

## Validator local estático opcional

```powershell
Unblock-File .\scripts\v1.1\validate-v1.1-02-production-metrics-alerting.ps1
```

```powershell
.\scripts\v1.1\validate-v1.1-02-production-metrics-alerting.ps1 -StaticOnly
```

Esperado:

`PASS V1.1-02 PRODUCTION METRICS & ALERTING STATIC CONTRACT`

## Validación post-deploy

Usar un JWT válido con permiso `reports.read` almacenado únicamente en memoria en `$token`.

```powershell
.\scripts\v1.1\validate-v1.1-02-production-metrics-alerting.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -BearerToken $token
```

Esperado:

`PASS V1.1-02 PRODUCTION METRICS & ALERTING`

No imprimir `$token`.

## Si falla

Enviar:

- comando ejecutado;
- exit code;
- step exacto de GitHub Actions;
- error completo;
- para fallo HTTP: status code y body sanitizado;
- para fallo de backend: logs relacionados con trace/correlation ID sin secretos.

No cambiar thresholds ni código antes del diagnóstico de ChatGPT Web.
