# Macro Fase 30 — Contract Parity Hardening

Estado: IMPLEMENTED — pending local validation

## Objetivo

Cerrar la brecha entre:

- OpenAPI
- endpoints reales en runtime
- roadmap

La regla de esta fase es que el contrato público no debe documentar rutas que no existen, y el runtime no debe exponer rutas no documentadas.

## Decisión arquitectónica

OpenAPI se convierte en contrato runtime, no en roadmap.

Los endpoints futuros deben vivir en roadmap o ADR, no en `contracts/openapi/solidpos-api-v1.openapi.yaml` hasta que estén implementados.

## Cambios

### Contract tests

Archivo endurecido:

- `tests/SolidPOS.PosServer.ContractTests/OpenApiContractTests.cs`

Nuevas validaciones:

- El archivo OpenAPI existe y tiene paths.
- Los endpoints reales del runtime coinciden con OpenAPI por path y método.
- Se detectan rutas implementadas no documentadas.
- Se detectan rutas documentadas no implementadas.
- Se valida que cada operación tenga `operationId`.
- Se valida que cada operación tenga respuesta 2xx.
- Se valida que las operaciones mutantes tengan `requestBody` cuando aplica.
- Se valida que exista contrato de errores ProblemDetails.
- Se valida que las respuestas compartidas usen `application/problem+json`.

### Project references

Actualizado:

- `tests/SolidPOS.PosServer.ContractTests/SolidPOS.PosServer.ContractTests.csproj`

Agregado:

- `Microsoft.AspNetCore.Mvc.Testing`
- referencia al proyecto `SolidPOS.PosServer.Api`

Esto permite inspeccionar `EndpointDataSource` y comparar el runtime real contra OpenAPI.

### OpenAPI

Archivo actualizado:

- `contracts/openapi/solidpos-api-v1.openapi.yaml`

Cambios clave:

- Eliminados endpoints de roadmap aún no implementados.
- Corregido `GET /sync/pull`.
- Agregado `POST /sync/process`.
- Agregados endpoints de inventory policy, counts, transfers y low-stock.
- Agregados endpoints de sync conflicts y bootstrap.
- Agregado `GET /system/info`.
- Corregidas rutas de returns sin prefijo duplicado `/api/v1`.
- Agregado `PATCH /discounts/{discountId}`.
- Agregada respuesta compartida `UnexpectedError` con ProblemDetails.

## Endpoints actualmente cubiertos por paridad

- health
- system
- auth
- terminals
- terminal runtime
- tenant config/catalog runtime
- customers
- discounts
- cash drawers
- inventory
- sales
- receipts
- returns
- sync
- admin mutations
- audit
- reports

## Regla SDLC

A partir de esta fase, cuando se agregue un endpoint nuevo:

1. Implementar runtime.
2. Actualizar OpenAPI.
3. Agregar/actualizar ContractTests si cambia estructura relevante.
4. Ejecutar `dotnet test solidpos-platform.sln`.

Si el contrato se queda atrás, `SolidPOS.PosServer.ContractTests` debe fallar.
