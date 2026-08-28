# SolidPOS PILOT-02 — Operator Checklist

## Antes de ejecutar

- [ ] Railway está desplegado.
- [ ] Supabase está disponible.
- [ ] `DATABASE_URL` corresponde a la credencial nueva rotada.
- [ ] `admin@micafeteria.com` puede iniciar sesión.
- [ ] PILOT-01 está cerrado como GO.
- [ ] No hay secretos pegados en archivos versionados.

## Durante la ejecución

- [ ] Confirmar que el dashboard compila.
- [ ] Confirmar que `/health/live` responde `alive`.
- [ ] Confirmar que `/health/ready` responde `ready`.
- [ ] Confirmar login admin PASS.
- [ ] Confirmar terminal registrada.
- [ ] Confirmar turno abierto.
- [ ] Confirmar venta creada.
- [ ] Confirmar cambio correcto.
- [ ] Confirmar recibo digital.
- [ ] Confirmar inventario descontado.
- [ ] Confirmar audit event.
- [ ] Confirmar turno cerrado con diferencia cero.

## Después de ejecutar

- [ ] Guardar logs de PowerShell.
- [ ] Revisar `docs/pilot/logs/pilot-02-transaction-log.md`.
- [ ] Commit y push si el resultado es GO.
- [ ] No avanzar a PILOT-03 si cualquier validación falló.
