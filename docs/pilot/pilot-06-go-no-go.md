# PILOT-06 GO / NO-GO

## GO

PILOT-06 es GO solo si se cumple todo:

- [ ] Production liveness/readiness PASS.
- [ ] Admin login PASS.
- [ ] Sync contract reporta schema version 4.
- [ ] Terminal runtime context valido.
- [ ] Stuck `processing` vencido termina en `processed`.
- [ ] Dead-letter aparece en API.
- [ ] Retry manual devuelve `retry_pending`.
- [ ] Reproceso controlado vuelve a `dead_letter` sin romper runtime.
- [ ] `sale.voided` controlado produce `conflict`.
- [ ] Conflicto pending aparece por API.
- [ ] Resolucion `use_server` deja conflicto `resolved`.
- [ ] Inbox asociado al conflicto queda `processed`.
- [ ] Audit trail contiene `sync.conflict.resolved`.
- [ ] SQL final devuelve `GO`.

## NO-GO

PILOT-06 es NO-GO si ocurre cualquiera:

- [ ] Error de auth o terminal context.
- [ ] Sync contract no es schema version 4.
- [ ] Recovery de processing no procesa el evento controlado.
- [ ] Retry dead-letter no programa `retry_pending`.
- [ ] Conflicto no aparece o no puede resolverse.
- [ ] SQL final devuelve `NO-GO`.
- [ ] Aparecen errores no scoped que bloquean el runtime.
