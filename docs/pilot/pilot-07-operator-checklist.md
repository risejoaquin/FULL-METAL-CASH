# PILOT-07 Operator Checklist

- [ ] Confirmar que PILOT-06 esta en PASS REAL PRODUCTION / GO.
- [ ] Cargar `$securePassword` con `Read-Host -AsSecureString`.
- [ ] Cargar `$env:DATABASE_URL` desde Supabase.
- [ ] Verificar que `$env:DATABASE_URL.Substring(0,13)` responde `postgresql://`.
- [ ] Ejecutar `Unblock-File` sobre el validador.
- [ ] Ejecutar `validate-dashboard-operations-monitoring.ps1`.
- [ ] Confirmar build production de PosDashboard.
- [ ] Confirmar `/api/v1/observability/metrics`.
- [ ] Confirmar SQL cross-check.
- [ ] Guardar salida final con `goNoGo = GO`.
