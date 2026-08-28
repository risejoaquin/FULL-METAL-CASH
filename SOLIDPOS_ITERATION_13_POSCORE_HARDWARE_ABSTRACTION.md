# SolidPOS Iteration 13 — PosCore Hardware Abstraction Runtime

## Objetivo

Agregar una capa local de abstracción de hardware para PosCore sin acoplarla a WPF ni a dispositivos reales todavía.

## Alcance implementado

- Abstracciones de hardware para:
  - impresora de recibos
  - cajón de efectivo
  - scanner de código de barras
  - terminal de pago
- Adapters fake para validación local.
- Cola local de trabajos de impresión en SQLite.
- Eventos locales de hardware en SQLite.
- Comandos CLI para validar hardware sin UI.
- Tests unitarios del runtime de hardware.
- Script E2E local de hardware.

## Tablas SQLite agregadas

```text
local_print_jobs
local_hardware_events
```

## Comandos CLI agregados

```text
queue-receipt-print
process-print-jobs
open-cash-drawer-hardware
scan-barcode
authorize-payment-terminal
hardware-status
```

## Decisión arquitectónica

La UI WPF no debe hablar directamente con impresoras, cajones, scanners o terminales de pago. WPF deberá consumir servicios de Application. Esta iteración deja esa frontera lista antes de iniciar WPF.

## Criterio PASS

```text
dotnet restore PASS
dotnet build PASS
dotnet test PASS
receipt print job queued
fake printer processed job
fake cash drawer command registered
fake barcode scanned
fake payment approved
hardware events recorded
hardware status reports printed job and events
```
