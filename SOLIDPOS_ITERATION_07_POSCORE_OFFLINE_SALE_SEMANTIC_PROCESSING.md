# SolidPOS Iteration 07 — PosCore Offline Sale Semantic Processing

## Objetivo

Procesar semánticamente una venta offline real generada por PosCore local (`sale.completed`) y convertirla en una venta materializada en PosServer.

La iteración valida el ciclo:

```text
offline sale local SQLite
→ local outbox event sale.completed
→ sync push remoto
→ sync process remoto
→ sale materializada en PosServer
→ recibo digital emitido
→ cash shift actualizado/cerrado
→ pull/status/dead-letter verificados
→ outbox local sin pendientes
```

## Decisiones técnicas

- `sale.completed` ahora usa payload compatible con `CreateSaleRequest` del PosServer.
- PosCore exige `cashierUserId` para sincronización semántica de ventas.
- El identificador de entidad de sync para ventas se resuelve desde `localSaleId`.
- La prueba E2E abre un cash shift remoto antes de procesar la venta offline.
- La prueba E2E busca la venta materializada por `localSaleId`.
- La prueba E2E emite recibo digital y cierra caja para dejar el ambiente limpio.

## Cambios principales

```text
src/PosCore/SolidPOS.PosCore.Domain/SaleDraft.cs
src/PosCore/SolidPOS.PosCore.Application/OfflineSales/OfflineSaleService.cs
src/PosCore/SolidPOS.PosCore.Application/Sync/RemoteSyncPushContracts.cs
src/PosCore/SolidPOS.PosCore.Cli/Program.cs
tests/SolidPOS.PosCore.UnitTests/OfflineSaleServiceSemanticPayloadTests.cs
scripts/poscore/validate-poscore-offline-sale-semantic-processing.ps1
```

## Validación esperada

```text
dotnet restore                         PASS
dotnet build                           PASS
dotnet test                            PASS
Smoke remoto Railway                    PASS
PosCore offline sale semantic E2E       PASS
```

Resultado esperado del E2E:

```text
message: PosCore offline sale semantic processing completed.
processedCount >= 1
saleTotalCents = 4500
cashSalesCents = 4500
syncStatusDeadLetterCount = 0
deadLetterListCount = 0
```
