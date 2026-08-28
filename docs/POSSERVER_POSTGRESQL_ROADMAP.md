# Roadmap PosServer + PostgreSQL - SolidPOS MVP

## 1. Objetivo

Construir primero `PosServer` con PostgreSQL/Supabase hasta tener una API backend robusta, observable, testeable y lista para ser consumida despues por `PosCore`, `PosLocal`, `PosBuilder` y `PosDashboard`.

No se avanza a `PosCore + SQLite` hasta que esta fase este terminada al 100%.

## 2. Alcance de PosServer

`PosServer` debe cubrir:

- multi-tenant
- PostgreSQL
- RLS
- migraciones
- autenticacion
- refresh-token rotation
- RBAC
- tenant context
- catalogo
- categorias
- productos
- variantes
- codigos de barras
- precios
- modificadores
- recetas/BOM
- caja
- ventas
- pagos
- inventario ledger
- recibo digital
- terminal enrollment
- terminal revoke
- sync inbox/outbox cloud
- idempotencia
- audit logs
- observabilidad
- OpenAPI
- pruebas unitarias
- pruebas de integracion
- pruebas de contrato
- Docker
- Railway-ready
- ZIP entregable

## 3. Principios Tecnicos

1. Todo endpoint multi-tenant debe resolver `tenant_id` desde contexto autenticado.
2. Ninguna query operativa debe confiar en `tenant_id` enviado por cliente.
3. PostgreSQL debe aplicar RLS en tablas operativas.
4. Todo comando critico debe ser idempotente.
5. Ventas, pagos, caja e inventario deben ejecutarse en transacciones ACID.
6. Inventario se registra por `inventory_ledger` append-only.
7. No hay `DELETE` fisico en entidades historicas.
8. Toda excepcion debe generar log estructurado.
9. Todo request debe tener `trace_id` o `correlation_id`.
10. El ZIP entregable debe incluir codigo, migraciones, README, scripts y guia de prueba.

## 4. Stack PosServer

| Capa | Decision |
| --- | --- |
| Lenguaje | C# |
| Framework | ASP.NET Core Web API |
| Plataforma | .NET 10 LTS, compatible en transicion desde .NET 8 |
| Arquitectura | Clean/Hexagonal modular |
| DB | PostgreSQL |
| Proveedor objetivo | Supabase |
| Data access | EF Core + SQL explicito en rutas criticas |
| Auth | JWT + refresh-token rotation |
| Password hashing | BCrypt |
| Logging | Serilog JSON |
| Observabilidad | OpenTelemetry |
| API docs | OpenAPI/Swagger versionado |
| Tests | xUnit + Moq + integration tests |
| Deploy | Docker + Railway |

## 5. Estructura Objetivo

```text
solidpos-platform/
  docs/
  contracts/
    openapi/
      solidpos-api-v1.openapi.yaml
  database/
    postgresql/
      001_initial_schema_postgresql.sql
      002_seed_permissions.sql
      003_seed_mvp_defaults.sql
  src/
    PosServer/
      SolidPOS.PosServer.Api/
      SolidPOS.PosServer.Application/
      SolidPOS.PosServer.Domain/
      SolidPOS.PosServer.Infrastructure/
      SolidPOS.PosServer.Contracts/
  tests/
    SolidPOS.PosServer.UnitTests/
    SolidPOS.PosServer.IntegrationTests/
    SolidPOS.PosServer.ContractTests/
  deploy/
    docker/
    railway/
  scripts/
```

## 6. Fase 0 - Preparacion del Backend

Objetivo:

Crear la base del proyecto y dejarlo compilando.

Entregables:

- solucion `.sln`
- proyectos `Api`, `Application`, `Domain`, `Infrastructure`, `Contracts`
- `.editorconfig`
- `Directory.Build.props`
- health endpoint inicial
- Swagger habilitado por ambiente
- Serilog configurado
- OpenTelemetry base
- Dockerfile
- README de ejecucion local

Definition of Done:

- `dotnet build` pasa
- `dotnet test` pasa aunque no haya pruebas funcionales todavia
- `/health` responde
- logs salen en JSON
- Swagger abre en local

## 7. Fase 1 - PostgreSQL y Migraciones

Objetivo:

Dejar PostgreSQL listo con schema base.

Entregables:

- migracion `001_initial_schema_postgresql.sql`
- seeds de permisos
- seeds de roles MVP
- seeds de unidades base
- seeds de metodos de pago MVP
- script de reset local
- documentacion de variables de entorno

Validaciones:

- tablas creadas
- indices creados
- RLS habilitado
- policies creadas
- `inventory_ledger` bloquea update/delete
- `sales` exige `cash_shift_id` en ventas no suspendidas
- `digital_receipts` existe

Definition of Done:

- migracion ejecuta limpia en PostgreSQL
- rollback o reset documentado
- pruebas de integracion pueden crear DB temporal o schema temporal

## 8. Fase 2 - Tenant Context + RLS

Objetivo:

Blindar multi-tenancy antes de endpoints de negocio.

Entregables:

- middleware de tenant context
- servicio `ITenantContext`
- seteo de `app.tenant_id` por transaccion/conexion
- claims obligatorios `user_id`, `tenant_id`, `roles`
- tests RLS

Casos a probar:

- usuario tenant A no lee datos tenant B
- query sin tenant context falla o no devuelve datos
- endpoint rechaza JWT sin `tenant_id`
- endpoint rechaza JWT sin `user_id`

Definition of Done:

- pruebas de aislamiento multi-tenant pasan
- logs incluyen `tenant_id`, `user_id`, `trace_id`

## 9. Fase 3 - Auth, Refresh Tokens y RBAC

Objetivo:

Implementar autenticacion robusta.

Entregables:

- `POST /api/v1/auth/login`
- `POST /api/v1/auth/refresh`
- `POST /api/v1/auth/logout`
- BCrypt parametrizado
- dummy password verification para evitar user enumeration
- refresh-token rotation
- revocacion de tokens
- roles MVP: Owner, Admin, Manager, Cashier
- policies por permiso

Logs obligatorios:

- login exitoso
- login fallido sin revelar si usuario existe
- refresh exitoso
- refresh reuse detectado
- logout

Definition of Done:

- no hay mensajes que permitan enumerar usuarios
- refresh token viejo no puede reusarse
- JWT incluye claims requeridos
- policies funcionan por rol

## 10. Fase 4 - Terminal Enrollment

Objetivo:

Permitir vinculacion segura de terminales.

Entregables:

- `POST /api/v1/terminals/enrollment-tokens`
- `POST /api/v1/terminals/register`
- `POST /api/v1/terminals/{terminalId}/revoke`
- `POST /api/v1/terminals/{terminalId}/heartbeat`
- device token hash
- revocacion remota
- estado de terminal
- offline grace fields

Logs obligatorios:

- token emitido
- terminal vinculada
- terminal revocada
- heartbeat recibido
- intento con token vencido

Definition of Done:

- terminal puede registrarse con token valido
- token vencido falla
- terminal revocada no puede sincronizar

## 11. Fase 5 - Catalogo MVP

Objetivo:

Crear administracion de catalogo para QSR/cafeteria.

Entregables:

- categorias
- productos
- variantes
- codigos de barras
- listas de precios
- precios
- modificadores
- grupos de modificadores
- recetas
- ingredientes de receta

Endpoints:

- `GET/POST/PUT /api/v1/categories`
- `GET/POST/PUT /api/v1/products`
- `GET /api/v1/products/barcode/{barcode}`
- `GET/POST/PUT /api/v1/modifier-groups`
- `GET/POST/PUT /api/v1/recipes`
- `GET/POST/PUT /api/v1/price-lists`

Definition of Done:

- se puede configurar un Latte con receta
- se puede configurar modificador leche de avena
- barcode lookup funciona
- soft delete funciona
- cambios generan `sync_changes`

## 12. Fase 6 - Caja

Objetivo:

Implementar control financiero por turno.

Endpoints:

- `POST /api/v1/cash-shifts/open`
- `POST /api/v1/cash-shifts/{id}/movements`
- `POST /api/v1/cash-shifts/{id}/close`
- `GET /api/v1/cash-shifts`

Reglas:

- no hay venta sin turno abierto
- solo un turno abierto por terminal
- cierre calcula diferencia
- movimientos requieren razon

Definition of Done:

- turno abre
- turno bloquea duplicado abierto
- movimiento cash-in/cash-out queda auditado
- cierre calcula expected vs counted

## 13. Fase 7 - Ventas, Pagos e Inventario Ledger

Objetivo:

Procesar ventas liquidadas y afectar inventario.

Endpoints:

- `POST /api/v1/sales`
- `GET /api/v1/sales/{id}`
- `POST /api/v1/sales/{id}/void`
- `POST /api/v1/payments`

Reglas:

- venta requiere turno abierto
- venta acepta efectivo, tarjeta manual y transferencia
- venta puede tener pagos mixtos
- venta con receta descuenta ingredientes
- modificador puede sumar precio y cambiar consumo
- inventario negativo permitido con auditoria
- todo en una transaccion ACID
- todo comando critico con `Idempotency-Key`

Logs obligatorios:

- venta recibida
- venta completada
- idempotency replay
- inventario negativo generado
- pago registrado
- error de validacion

Definition of Done:

- venta simple funciona
- venta con BOM funciona
- venta con modificador funciona
- ledger registra movimientos
- idempotencia evita duplicado
- recibo digital se genera

## 14. Fase 8 - Recibo Digital

Objetivo:

Soportar QR a visor web de recibo.

Endpoints:

- `GET /api/v1/receipts/{saleId}`
- `GET /r/{publicToken}`

Reglas:

- token publico no expone IDs internos
- token se guarda hasheado
- respuesta publica no incluye datos sensibles internos
- recibo refleja snapshot de venta

Definition of Done:

- venta genera recibo digital
- QR puede apuntar al visor
- token revocado no funciona

## 15. Fase 9 - Sync Cloud Inbox/Pull

Objetivo:

Preparar la nube para recibir eventos offline futuros.

Endpoints:

- `POST /api/v1/sync/push`
- `POST /api/v1/sync/pull`
- `GET /api/v1/sync/conflicts`
- `POST /api/v1/sync/conflicts/{id}/resolve`

Reglas:

- inbox idempotente por `(tenant_id, terminal_id, event_id)`
- batch processing
- resultado accepted/rejected/conflicts
- `sync_changes` para deltas
- terminal revocada no sincroniza

Definition of Done:

- batch duplicado no reprocesa
- pull devuelve cambios por cursor
- conflictos quedan registrados
- logs tienen payload hash, no payload sensible completo

## 16. Fase 10 - Auditoria, Logs y Observabilidad

Objetivo:

Que cada fallo que el usuario reporte pueda diagnosticarse con logs.

Requisitos Serilog:

- salida consola JSON
- `trace_id`
- `correlation_id`
- `tenant_id`
- `user_id`
- `terminal_id`
- `endpoint`
- `status_code`
- `elapsed_ms`
- exception stack trace
- request id

Requisitos OpenTelemetry:

- traces HTTP
- traces DB
- metrics basicas
- health checks

No loggear:

- passwords
- refresh tokens
- access tokens
- enrollment tokens
- device tokens
- datos de tarjeta

Definition of Done:

- error 500 genera log con stack trace
- error 400 genera log de validacion
- cada request tiene correlation id
- logs permiten rastrear tenant/terminal

## 17. Fase 11 - OpenAPI y Contratos

Objetivo:

Congelar contrato API consumible por POS y dashboard.

Entregables:

- `solidpos-api-v1.openapi.yaml`
- Swagger agrupado por tags
- ProblemDetails estandar
- ejemplos request/response
- contract tests

Definition of Done:

- OpenAPI valida
- endpoints documentados
- DTOs no filtran entidades internas

## 18. Fase 12 - Pruebas

Tipos:

- unitarias
- integracion con PostgreSQL
- contrato OpenAPI
- seguridad/RLS
- idempotencia
- transacciones ACID

Pruebas obligatorias:

- login no enumera usuarios
- refresh rotation bloquea reuse
- tenant A no lee tenant B
- venta sin caja falla
- venta con caja pasa
- venta duplicada por idempotency key no duplica
- ledger append-only no permite update/delete
- terminal revocada no sincroniza
- sync push duplicado no reprocesa
- recibo digital publico no expone datos internos

## 19. Fase 13 - Docker + Railway

Objetivo:

Entregar backend listo para correr.

Entregables:

- Dockerfile
- `docker-compose.yml` local con PostgreSQL
- variables `.env.example`
- Railway config
- health checks
- migration runner documentado

Definition of Done:

- API corre local con Docker
- API conecta a PostgreSQL
- migraciones ejecutan
- `/health` y `/health/ready` responden

## 20. Fase 14 - ZIP Entregable

Objetivo:

Entregar al usuario un ZIP descargable para pruebas.

Debe incluir:

- codigo fuente
- migraciones
- OpenAPI
- README
- `.env.example`
- scripts de ejecucion
- guia de pruebas
- guia de logs
- docker-compose

El usuario probara localmente y devolvera logs.

Definition of Done:

- ZIP abre correctamente
- instrucciones son reproducibles
- logs suficientes para diagnosticar errores

## 21. Orden de Implementacion

1. Base proyecto y logging.
2. PostgreSQL/migraciones.
3. Tenant context/RLS.
4. Auth/RBAC.
5. Terminal enrollment.
6. Catalogo.
7. Caja.
8. Ventas/pagos/inventario ledger.
9. Recibo digital.
10. Sync cloud.
11. Auditoria/observabilidad.
12. OpenAPI.
13. Tests.
14. Docker/Railway.
15. ZIP entregable.

## 22. Criterio de Finalizacion 100%

PosServer + PostgreSQL se considera terminado cuando:

- compila sin errores
- pruebas pasan
- migracion PostgreSQL ejecuta limpia
- RLS probado
- auth probado
- RBAC probado
- catalogo MVP funcional
- caja funcional
- ventas funcionales
- pagos funcionales
- inventario ledger funcional
- recibo digital funcional
- sync cloud preparado
- logs estructurados completos
- OpenAPI actualizado
- Docker listo
- README reproduce entorno
- ZIP entregado

---

## Estado de implementación incremental - Macro Fase 22

La Macro Fase 22 endurece la implementación de reportes correspondiente a las fases de Ventas/Inventario, OpenAPI y Pruebas del roadmap sin alterar el orden funcional pendiente.

Cambios cerrados en esta iteración:

- exactitud de reportes financieros y reconciliación de efectivo/cambio
- validación tenant-scoped de `storeId`
- read model consolidado para Dashboard React
- trazabilidad de BOM/modificadores en movimientos de inventario
- semántica explícita `none|add|substitute` para efectos de inventario de modificadores
- corrección de sustitución de leche del Latte demo

El siguiente trabajo debe continuar sobre los módulos pendientes del roadmap; esta macrofase no declara PosServer finalizado.


## Macro Fase 23 — Sales Retrieval + Receipt Read Models

Status: IMPLEMENTED — pending local validation

Esta fase continúa después de Macro Fase 22 y deja cerrada la lectura operativa de ventas.

Endpoints implementados:

- `GET /api/v1/sales/{saleId}`
- `GET /api/v1/sales?from=&to=&storeId=&terminalId=&status=&limit=`
- `GET /api/v1/receipts/{saleId}`

Decisión registrada desde Macro Fase 22:

- La semántica de modificadores `none | add | substitute` está implementada, migrada, sembrada y validada manualmente.
- Los read models de venta usan snapshots de línea para reconstruir modifiers históricos.
- Los movimientos de inventario asociados salen del ledger append-only.

Siguiente fase natural:

- Macro Fase 24 — Digital Receipts: persistencia/URL pública/email de recibo no fiscal.

### Macro Fase 23 Hotfix 23.1 note

The sales list read model is tolerant of legacy/local historical rows where `cash_shift_id` may be null. This keeps creation/void invariants intact while preventing `GET /api/v1/sales` from failing with HTTP 500 on old development data.

### Macro Fase 23 Hotfix 23.3 note

`GET /api/v1/sales` list reader lifetime was hardened after local validation found an `NpgsqlOperationInProgressException` during transaction commit. The list reader is now explicitly disposed before commit, preserving transaction/RLS behavior while preventing active-command commit failures.


## Registro Macro Fase 24

Macro Fase 24 agrega recibos POS digitales no fiscales: persistencia en `digital_receipts`, `receipt_number`, `public_token` con hash almacenado, `public_url`, estado de emisión, lectura pública/protegida, reenvío/email stub auditable, contrato OpenAPI y tests. No incluye SAT/facturación fiscal.
