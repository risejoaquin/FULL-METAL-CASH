# SolidPOS PILOT-02 Hotfix 02.6 — SQL Assertion Without Constant Division

## Problema

La validación SQL de PILOT-02 imprimía `pilot_02_go_no_go = GO`, pero después fallaba con `division by zero`.

La causa era el uso de `ELSE CAST(1 / 0 AS text)` dentro de un `CASE`. PostgreSQL puede evaluar expresiones constantes durante planificación/optimización, por lo que la división por cero puede dispararse aunque la rama GO ya sea verdadera.

## Corrección

- Se eliminó la división por cero como mecanismo de fallo.
- El SQL ahora devuelve explícitamente `PILOT-02 real POS transaction validation PASS` y `GO`.
- El script PowerShell captura la salida de `psql`, valida que contenga `PASS` y `GO`, y solo falla si el proceso SQL falla o si la aserción no reporta GO.

## Alcance

No cambia backend, endpoints, base de datos, venta, caja, pagos, recibos ni dashboard. Solo endurece el contrato del validador productivo PILOT-02.
