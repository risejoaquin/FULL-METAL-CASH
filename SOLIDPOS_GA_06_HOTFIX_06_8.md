# SolidPOS — HOTFIX GA-06.8

## Explicit release reconciliation conflict diagnostics

GA-06.7 demostró una discrepancia entre el drift check SQL y el rechazo HTTP de `POST /updates/releases`: el SQL reportó PASS pero el endpoint devolvió HTTP 409.

GA-06.8 no evita el conflicto. Hace que el backend exponga una clasificación segura y determinista del rechazo para poder resolver la causa real sin adivinar ni modificar datos productivos.

### Contrato añadido

- `INVALID_TARGET_TERMINAL`: uno o más `targetTerminalIds` no son terminales activos del tenant visto por la conexión del API.
- `RELEASE_IDENTITY_CONFLICT`: existe una release con la misma clave natural pero su identidad no coincide exactamente. El response incluye únicamente los nombres de los campos divergentes.

Campos comparados:

- tenant scope
- version
- channel
- packageType
- artifactUrl
- artifactHash
- signature
- rollbackVersion
- mandatory
- universalInstaller
- revokedAt

### Seguridad

No se exponen secretos. El response no devuelve firmas completas, hashes completos ni credenciales; solo nombres de campos en conflicto y los identificadores operativos channel/version/packageType.

### Persistencia

No hay UPDATE/DELETE correctivo. Si hay conflicto, la transacción se revierte antes de insertar targets.
