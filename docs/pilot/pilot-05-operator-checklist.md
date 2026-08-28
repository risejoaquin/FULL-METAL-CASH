# PILOT-05 Operator Checklist — Offline Mode Field Test

## Antes de ejecutar

- [ ] Confirmar que se está usando el ZIP de PILOT-05.
- [ ] Confirmar que no se guardaron contraseñas ni secretos en archivos.
- [ ] Confirmar `DATABASE_URL` con prefijo `postgresql://`.
- [ ] Confirmar Docker Desktop activo.
- [ ] Confirmar .NET 8 disponible.
- [ ] Confirmar conectividad inicial a Railway/Supabase.

## Durante la prueba

- [ ] Registrar terminal productiva controlada.
- [ ] Descargar bootstrap de sync.
- [ ] Cachear usuario local offline.
- [ ] Validar login local.
- [ ] Descargar catálogo local.
- [ ] Descargar inventario local.
- [ ] Crear venta offline cash desde cache.
- [ ] Confirmar outbox pendiente.
- [ ] Confirmar movimiento local de inventario.
- [ ] Reconectar y ejecutar push sync.
- [ ] Procesar batch remoto.
- [ ] Reenviar evento para validar idempotencia.
- [ ] Ejecutar pull sync dos veces.
- [ ] Emitir recibo digital.
- [ ] Procesar print job local.
- [ ] Cerrar caja local/remota en diferencia cero.
- [ ] Ejecutar SQL final.

## Cierre

- [ ] Guardar `docs/pilot/logs/pilot-05-offline-mode-field-test-log.md`.
- [ ] Reportar `GO`, `NO-GO` o `FAIL` con salida completa.
