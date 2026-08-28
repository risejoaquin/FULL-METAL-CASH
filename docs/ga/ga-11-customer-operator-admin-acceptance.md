# GA-11 — Customer, Operator and Admin Acceptance

## Propósito

GA-11 valida que SolidPOS puede ser aceptado por tres perspectivas reales:

- customer acceptance;
- operator acceptance;
- admin acceptance.

El gate no activa General Availability pública. El avance permitido es únicamente hacia GA-12.

## Criterios de aceptación

### Customer acceptance

- El API de customers responde correctamente.
- Los flujos de receipt, returns y soporte quedan documentados en la checklist.
- Las condiciones conocidas se comunican sin ocultarlas.

### Operator acceptance

- El operador puede depender de catálogo, ventas, reportes, inventario y sync.
- `schemaVersion=4` y `syncContract=schema_version_4` se conservan.
- Offline/sync no presenta conflictos pendientes.

### Admin acceptance

- Tenant, stores, users, roles, permissions, dashboard y observability responden.
- La condición GA-09 `Concurrency 3+` / `400 upstream error` queda arrastrada.
- La observación GA-10 `db_waiting_connections_11` queda arrastrada.

## No negociable

- `generalAvailabilityActivated=False`.
- RLS sin drift.
- Sin duplicación de ventas/pagos.
- Sin secretos en logs.
- Sin cambios destructivos en datos productivos.
