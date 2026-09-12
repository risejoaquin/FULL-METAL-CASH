# V1.1-04 Validation Commands

```powershell
Unblock-File .\scripts\v1.1\validate-v1.1-04-postgresql-query-hardening.ps1
.\scripts\v1.1\validate-v1.1-04-postgresql-query-hardening.ps1 -StaticOnly
```

Luego: restore, build, test, migraciones, CI, deploy y producción. Para producción el validator requiere `BaseUrl`, `TenantId`, `Email` y `Password` y exige p95 <= 1200 ms, cero idle-in-transaction inesperadas y cero server waits activos no-client.
