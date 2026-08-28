# PILOT-05 GO / NO-GO

## GO

PILOT-05 es `GO` si el validador termina con:

```text
[PILOT-05] PILOT-05 PASS REAL PRODUCTION / GO
```

Y el SQL final contiene:

```text
pilot_05_go_no_go | GO
```

## NO-GO

PILOT-05 es `NO-GO` si cualquiera de estas validaciones falla:

- Sync contract no reporta schema version 4.
- Bootstrap no coincide con tenant/store/terminal.
- Login local no autoriza `sales.create`.
- Catálogo local no contiene el SKU objetivo.
- Inventario local no tiene recipe items para el SKU objetivo.
- Outbox no se genera.
- Sync push/process no procesa `sale.completed`.
- Duplicate push genera rejected/dead-letter.
- Pull sync rompe cursor o no aplica cambios.
- Venta remota no aparece por `localSaleId`.
- Recibo digital no queda activo.
- Inventario remoto no registra ledger por venta.
- Shift remoto no cierra con diferencia cero.
- SQL final devuelve `NO-GO`.

## Estado actual

`PENDING USER VALIDATION`
