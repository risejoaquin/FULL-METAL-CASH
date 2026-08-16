# Diseno de Base de Datos - POS SaaS Offline-First

## 1. Objetivo del Modelo

La base de datos central debe soportar multiples comercios con giros distintos sin crear esquemas separados por cliente. El mismo modelo debe servir para sushi, cafeteria, ferreteria, abarrotes, retail general y servicios.

El diseno combina:

- modelo relacional fuerte para operaciones criticas
- JSONB para configuracion dinamica y atributos variables
- ledger append-only para inventario
- RLS para aislamiento multi-tenant
- idempotencia para sync offline
- soft delete para conservar historia operativa

## 2. Decisiones Arquitectonicas

### 2.1 Multi-Tenant con `tenant_id`

Todas las tablas operativas incluyen `tenant_id`.

Regla:

- ningun endpoint debe confiar en `tenant_id` enviado por el cliente si ya existe JWT/terminal token
- el backend debe resolver `tenant_id` desde el contexto autenticado
- cada transaccion debe ejecutar `SET LOCAL app.tenant_id = '<uuid>'`
- PostgreSQL RLS filtra filas usando `pos.current_tenant_id()`

### 2.2 RLS

El esquema habilita Row-Level Security en las tablas operativas.

Esto evita que un bug de query como:

```sql
SELECT * FROM products;
```

devuelva productos de otro comercio, siempre que la conexion tenga `app.tenant_id` configurado.

### 2.3 JSONB Controlado

JSONB se usa para flexibilidad, no para reemplazar el modelo relacional.

Uso correcto:

- configuracion de UI
- modulos activos
- hardware profile
- atributos dinamicos de producto
- metadata de pagos
- snapshots historicos

Uso incorrecto:

- totales de venta
- movimientos de inventario
- pagos
- permisos
- relaciones principales

### 2.4 Inventario por Ledger

`inventory_ledger` es append-only.

No se actualiza ni elimina. Cada cambio de stock se expresa como movimiento:

| Movimiento | Cantidad |
| --- | ---: |
| compra | positiva |
| venta | negativa |
| receta | negativa |
| devolucion | positiva |
| merma | negativa |
| ajuste | positiva o negativa |
| transferencia salida | negativa |
| transferencia entrada | positiva |
| conteo fisico | diferencia |

El stock actual se calcula como:

```sql
SUM(quantity_delta)
```

Ventaja:

- trazabilidad completa
- reversos por compensacion
- menos riesgo de corrupcion
- auditoria natural
- permite reconstruir stock historico

### 2.5 Snapshot de Ticket

Las lineas de venta guardan `snapshot`.

Esto conserva:

- nombre del producto al momento de venta
- precio usado
- receta usada
- modificadores
- impuestos internos
- unidad

Aunque el producto cambie despues, el ticket historico no cambia.

### 2.6 Idempotencia

Hay dos niveles:

1. `idempotency_keys` para endpoints HTTP directos.
2. `sync_inbox_events` para eventos offline del POS.

Restriccion principal:

```sql
UNIQUE (tenant_id, terminal_id, event_id)
```

Si una terminal reintenta el mismo evento por falla de red, el servidor responde OK sin reprocesar.

### 2.7 ACID

Las siguientes operaciones deben ejecutarse en una unica transaccion:

| Operacion | Debe incluir |
| --- | --- |
| Venta | sale, sale_lines, payments, inventory_ledger, cash update, sync result |
| Devolucion | return, return_lines, payment refund/manual record, inventory_ledger |
| Cierre caja | cash_shift, cash_movements, audit_event |
| Ajuste inventario | adjustment record, inventory_ledger, audit_event |
| Sync push batch | inbox events, domain effects, sync changes, conflicts |
| Registro terminal | terminal, device token hash, audit event |

Regla:

- si falla una parte critica, se hace rollback completo
- nunca debe quedar venta sin pagos esperados o inventario sin referencia

## 3. Dominios del Esquema

### 3.1 Tenant y Configuracion

Tablas:

- `tenants`
- `tenant_configs`
- `stores`

Soportan:

- comercio
- sucursales
- vertical de negocio
- modulos activos
- branding
- recibos
- hardware

Ejemplo:

```json
{
  "business_vertical": "retail",
  "ui_layout": "scanner_first",
  "modules_enabled": {
    "bulk_weight_sales": true,
    "customer_credit": true,
    "kds_kitchen_display": false
  }
}
```

### 3.2 Usuarios y Seguridad

Tablas:

- `users`
- `roles`
- `permissions`
- `role_permissions`
- `user_roles`
- `user_store_access`
- `refresh_tokens`
- `terminals`
- `enrollment_tokens`
- `terminal_offline_unlock_codes`

Separacion:

- usuarios humanos usan JWT + refresh token
- terminales usan device token
- primer arranque usa token de vinculacion de alcance limitado
- terminales pueden operar offline hasta 72 horas
- despues de 72 horas se aplica hard lock
- emergencia: codigo temporal telefonico registrado y auditado

### 3.3 Catalogo

Tablas:

- `categories`
- `unit_families`
- `units`
- `products`
- `product_variants`
- `product_barcodes`
- `price_lists`
- `product_prices`
- `modifier_groups`
- `modifiers`
- `product_modifier_groups`

Soporta:

- retail por barcode
- productos con atributos dinamicos
- venta por peso
- venta por metro
- variantes
- modificadores de cafeteria/restaurante
- multiples listas de precio

### 3.4 BOM y Recetas

Tablas:

- `recipes`
- `recipe_items`

Caso sushi:

- producto terminado: rollo
- ingredientes: arroz, alga, surimi, pepino
- al vender, se generan movimientos negativos por ingrediente

Caso cafeteria:

- producto terminado: latte
- ingredientes: cafe, leche, vaso, tapa
- modificador leche de avena puede sustituir leche normal

Caso ferreteria:

- kit: paquete de instalacion
- componentes: tornillos, taquetes, cable, servicio

### 3.5 Ventas, Pagos y Devoluciones

Tablas:

- `sales`
- `sale_lines`
- `payment_methods`
- `payments`
- `returns`
- `return_lines`
- `digital_receipts`

Soporta:

- tickets offline
- pagos mixtos
- propinas
- split bill a nivel de pagos
- devoluciones parciales
- cancelaciones compensadas
- recibo digital por QR

Regla de recibo digital:

- el QR apunta a un visor web de recibo
- no se exponen IDs internos como secreto
- el token publico se guarda hasheado
- el endpoint publico debe resolver el recibo mediante token y devolver solo datos seguros del ticket

### 3.6 Caja

Tablas:

- `cash_shifts`
- `cash_movements`

Regla:

- solo puede existir un turno abierto por terminal
- se conservan todos los turnos cerrados
- retiros e ingresos quedan auditados

### 3.7 Inventario

Tablas:

- `inventory_ledger`
- `purchase_orders`
- `purchase_order_lines`
- `stock_counts`
- `stock_count_lines`

Vista:

- `inventory_stock`

Regla:

- `inventory_stock` es lectura derivada
- `inventory_ledger` es la fuente de verdad

### 3.8 Sync

Tablas:

- `sync_inbox_events`
- `sync_changes`
- `sync_conflicts`
- `idempotency_keys`

Flujo push:

1. POS local envia batch.
2. Servidor valida terminal.
3. Servidor registra evento inbox.
4. Si es duplicado, devuelve resultado anterior.
5. Si es nuevo, aplica caso de uso.
6. Genera cambios para pull.
7. Responde accepted/rejected/conflicts.

Flujo pull:

1. Terminal envia cursor.
2. Servidor busca `sync_changes`.
3. Devuelve lote ordenado.
4. Terminal aplica inbox local.
5. Terminal guarda nuevo cursor.

### 3.9 POS Builder y Updates

Tablas:

- `builder_projects`
- `builder_builds`
- `update_releases`

Soporta:

- instalador universal Velopack
- vinculacion por token en primer arranque
- canales stable/beta/internal
- firma
- rollback
- artifact hash

Decision:

- no se compila un binario por tenant, sucursal o terminal
- el mismo `.setup.exe` se instala en todos los clientes
- la identidad de tenant/sucursal/terminal nace al vincularse con el servidor
- las diferencias visuales y funcionales vienen de `tenant_configs`

## 4. Politicas de Consistencia

### 4.1 Ventas

Una venta completada debe:

- insertar `sales`
- insertar `sale_lines`
- insertar `payments`
- insertar `inventory_ledger`
- registrar afectacion de caja si hay efectivo
- generar `sync_changes`
- generar `audit_events` si aplica

### 4.2 Inventario Negativo

Politica recomendada MVP:

- permitir negativo si ocurre durante operacion real
- registrar venta aunque genere negativo
- crear alerta/conflicto de inventario
- no bloquear venta offline

Razon:

- en POS offline, bloquear venta por stock desactualizado rompe la operacion
- la correccion debe hacerse por conteo/ajuste posterior
- inventario negativo es problema administrativo, no operativo

### 4.3 Last-Write-Wins

Se permite para:

- configuracion visual
- atributos de producto
- nombre/descripciones
- precios futuros

No se permite para:

- ventas
- pagos
- caja
- inventario ledger
- auditoria

En financieros se usan eventos compensatorios, no sobrescritura.

## 5. Indices Criticos

Todo indice operativo importante debe iniciar por `tenant_id`.

Ejemplos:

- productos por categoria
- barcode lookup
- ventas por sucursal/fecha
- pagos por venta
- movimientos por producto/sucursal
- sync changes por cursor
- inbox por estado
- audit por fecha

## 6. Reglas Para SQLite Local

La base local debe ser una proyeccion operativa, no una replica completa de administracion.

Debe contener:

- tenant config
- store config
- terminal state
- users autorizados para POS
- catalogo
- precios
- recetas
- modificadores
- inventario estimado
- ventas locales
- caja local
- pagos locales
- outbox
- sync cursor

No necesita contener:

- todos los tenants
- todos los reportes historicos
- refresh tokens admin
- builder builds globales

## 7. Pendientes Para Fase de Implementacion

Antes de codificar se deben crear:

- migracion PostgreSQL ejecutable
- migracion SQLite local equivalente
- seeds base de permisos
- seeds base de unidades
- seeds base de metodos de pago
- pruebas de migracion
- pruebas RLS
- pruebas de idempotencia
- pruebas de transacciones ACID
- pruebas de sync duplicate event
- pruebas de venta con receta
- prueba de hard lock offline 72 horas
- prueba de codigo temporal de desbloqueo offline

### 3.4.1 Semántica de inventario para modificadores

Desde Macro Fase 22, `modifiers` distingue explícitamente el efecto de inventario:

- `none`: el modificador solo afecta precio/presentación; no agrega consumo.
- `add`: agrega un ingrediente con `consumption_quantity` + `consumption_unit_id`.
- `substitute`: omite del BOM el ingrediente indicado por `replaces_product_id/replaces_variant_id` y consume en su lugar `linked_product_id/linked_variant_id`.

La cantidad de consumo se multiplica por la cantidad vendida. Cuando el producto tiene receta, el `waste_percent` de la receta se aplica también a ingredientes agregados o sustitutos.

Ejemplo Latte con leche de avena: el BOM base de 250 ml de leche entera es reemplazado por 250 ml de leche de avena; no se registran ambos consumos.
