# SolidPOS GA-07 — Backup, Restore, Rollback and Disaster Recovery Gate

## Objetivo

Probar que el estado productivo de SolidPOS puede respaldarse, restaurarse en aislamiento y recuperarse mediante un rollback de aplicación/release sin procedimientos improvisados ni mutaciones destructivas persistentes sobre producción.

## Entry gate

`PASS GA STABLE CHANNEL PROMOTION COHORT DRY RUN / GO GA-07`.

## Contrato de backup

GA-07 crea un backup lógico completo del schema `pos` usando PostgreSQL 17 `pg_dump` en formato custom. A diferencia del drill PILOT-08, el backup GA-07 contiene **schema + datos**, no solo DDL.

El backup debe:
- poder enumerarse con `pg_restore --list`;
- contener datos de tablas comerciales y de release;
- producir SHA-256 y tamaño verificables;
- no imprimir ni persistir `DATABASE_URL`;
- restaurarse únicamente en un PostgreSQL 17 efímero y aislado.

## Restore y reconciliación

El restore aislado debe conservar exactamente, para el tenant validado, los conteos de:
- tenant;
- stores;
- terminals;
- users;
- sales;
- payments;
- receipts;
- returns;
- refunds;
- inventory ledger;
- sync inbox;
- update releases;
- update release targets.

También debe contener la stable release validada en GA-06.

No se ejecuta restore sobre producción.

## RPO / RTO del drill

Para este gate controlado:
- `RPO target <= 300 seconds` medido desde el source snapshot productivo hasta la finalización del backup lógico;
- `RTO target <= 900 seconds` medido desde el inicio del entorno de restore hasta finalizar la reconciliación del restore.

Estos valores son objetivos del drill GA-07, no una afirmación histórica de SLA de producción.

## Application / Velopack rollback

La stable release debe conservar:
- `mandatory = false`;
- package `velopack`;
- `rollbackVersion` no vacío;
- al menos un release activo de rollback con artifact URL, SHA-256 y signature;
- universal installer.

El drill revoca temporalmente la stable release dentro de una transacción PostgreSQL, confirma que el rollback target existe y ejecuta `ROLLBACK`.

Resultado obligatorio:

`persistedRollbackMutationCount = 0`.

Después del drill, `/api/v1/updates/check` para el terminal controlado debe seguir resolviendo la misma stable release.

## Auditabilidad

Tras un drill exitoso se agrega un único evento append-only:

`ga07.rollback_drill.validated`

Este evento registra únicamente evidencia operacional no secreta: stable release, rollback version, RTO, RPO, restore GO y `persistedRollbackMutationCount = 0`.

## Seguridad

GA-07 prohíbe:
- `-ResetSchema`;
- restore productivo;
- delete de datos comerciales;
- rewrite del inventory ledger;
- revoke persistente de stable;
- activar General Availability;
- rollout público.

`schemaVersion = 4` y `syncContract = schema_version_4` permanecen invariantes.

## Blockers

- backup ilegible;
- restore no ejecutable;
- discrepancia de datos restaurados;
- RPO > 300 s;
- RTO > 900 s;
- rollback artifact ausente/inválido;
- stable release no recuperada después del transaction drill;
- `persistedRollbackMutationCount != 0`;
- health/readiness no ready después del drill;
- pérdida o mutación inesperada de datos productivos;
- audit evidence ausente.

## Resultado requerido

```text
PASS GA BACKUP RESTORE ROLLBACK DISASTER RECOVERY / GO GA-08
```

GA-07 no activa General Availability.
