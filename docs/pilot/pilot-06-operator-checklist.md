# PILOT-06 Operator Checklist

- [ ] Estoy en `C:\Users\Lucilfer\Documents\SolidPos`.
- [ ] Cargue `$securePassword` con `Read-Host -AsSecureString`.
- [ ] Cargue `$env:DATABASE_URL` sin pegarlo en logs.
- [ ] `$env:DATABASE_URL.Substring(0,13)` devuelve `postgresql://`.
- [ ] Ejecute `Unblock-File` para los scripts de PILOT-06, security y PosDashboard.
- [ ] `dotnet build solidpos-platform.sln` termina sin errores.
- [ ] `dotnet test solidpos-platform.sln` termina sin tests fallidos.
- [ ] Ejecute `validate-sync-recovery-conflict-field-test.ps1`.
- [ ] El resultado final dice `PILOT-06 PASS REAL PRODUCTION / GO`.
- [ ] Guarde la salida completa de PowerShell si aparece FAIL.
