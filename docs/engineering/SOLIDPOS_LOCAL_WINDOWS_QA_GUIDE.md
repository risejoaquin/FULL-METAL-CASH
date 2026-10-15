# SolidPOS — guía de prueba local Windows (AC-DEV)

**Estado:** laboratorio, no producción. **Objetivo:** probar la aplicación WPF comercial de una tienda/terminal y efectivo usando datos descartables. **Reglas:** no cambiar `main`, no Railway/Supabase reales, no credenciales reales, no borrar ni reescribir una SQLite existente.

## 0. Preparativos

Usar Windows 10/11 y PowerShell, Git, .NET SDK 8 y Docker Desktop sólo para el recorrido local completo. Crear idealmente **un usuario de Windows dedicado a QA**, ya que WPF usa de forma fija `%LOCALAPPDATA%\SolidBit\SolidPOS\terminal\poscore.sqlite`.

```powershell
git --version
dotnet --list-sdks
# Sólo para la segunda fase:
docker version
```

La carpeta del repo es configurable; el ejemplo usa `Documents\FULL-METAL-CASH`.

```powershell
$Repo = Join-Path $env:USERPROFILE 'Documents\FULL-METAL-CASH'
if (-not (Test-Path (Join-Path $Repo '.git'))) {
    git clone --branch AC-DEV --single-branch https://github.com/risejoaquin/FULL-METAL-CASH.git $Repo
}
Set-Location $Repo
git status --short
git branch --show-current
git rev-parse HEAD
```

Si ya hay cambios no guardados, no hacer `reset --hard`, `clean -fd` ni descartar trabajo. Revisar antes de actualizar. Asegurar que la rama sea `AC-DEV`.

## 1. Prueba rápida sin Docker: QA + interfaz

Eliminar variables heredadas que pudieran redirigir pruebas a una base remota.

```powershell
Remove-Item Env:SOLIDPOS_TEST_POSTGRES -ErrorAction SilentlyContinue
Remove-Item Env:DATABASE_URL -ErrorAction SilentlyContinue
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force .runtime\local-qa | Out-Null
dotnet restore solidpos-platform.sln
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore falló' }
dotnet build src/PosCore/SolidPOS.PosCore.Wpf/SolidPOS.PosCore.Wpf.csproj -c Debug
if ($LASTEXITCODE -ne 0) { throw 'WPF build falló' }
dotnet test tests/SolidPOS.PosCore.UnitTests/SolidPOS.PosCore.UnitTests.csproj -c Debug --logger 'trx;LogFileName=poscore-local.trx' --results-directory .runtime\local-qa
if ($LASTEXITCODE -ne 0) { throw 'Pruebas PosCore fallaron' }
& .\scripts\poscore\validate-poscore-wpf-sales-flow-qsr.ps1
if ($LASTEXITCODE -ne 0) { throw 'Self-test WPF falló' }
```

La prueba `--self-test` sólo valida un shell de **simulación**, no una venta comercial real. Los tests de integración PostgreSQL que requieran `SOLIDPOS_TEST_POSTGRES` no deben apuntar jamás a un servidor productivo.

Para ver la interfaz real (sin simulación):

```powershell
dotnet run --project src/PosCore/SolidPOS.PosCore.Wpf/SolidPOS.PosCore.Wpf.csproj
```

Si la terminal aún no tiene binding, usuario ni catálogo, debe mostrar **activación requerida y cobro bloqueado**. Es el comportamiento seguro esperado.

## 2. Laboratorio completo con API/PostgreSQL locales

**Sólo en entorno dedicado a QA.** No usar producción ni `-ResetSchema` para una base que se quiera conservar. Comprobar que Docker Desktop esté iniciado y que los puertos 5432/8080 estén disponibles.

```powershell
Set-Location $Repo
Remove-Item Env:DATABASE_URL -ErrorAction SilentlyContinue
Remove-Item Env:SOLIDPOS_TEST_POSTGRES -ErrorAction SilentlyContinue
docker compose up -d postgres
if ($LASTEXITCODE -ne 0) { throw 'Postgres Docker no inició' }
.\scripts\apply-postgresql-migrations.ps1
.\scripts\apply-dev-auth-seed.ps1
docker compose up -d posserver
if ($LASTEXITCODE -ne 0) { throw 'PosServer no inició' }
Invoke-RestMethod http://localhost:8080/health/ready
```

El usuario/password/IDs que siguen están documentados en `README.md` para **desarrollo exclusivamente**. No emplearlos en otros entornos.

```powershell
$Base = 'http://localhost:8080'
$TenantId = '11111111-1111-1111-1111-111111111111'
$StoreId = '22222222-2222-2222-2222-222222222222'
$DevEmail = 'owner@solidpos.local'
$DevPassword = 'Admin123!'
$body = @{ email=$DevEmail; password=$DevPassword; tenantId=$TenantId } | ConvertTo-Json
$owner = Invoke-RestMethod -Method Post -Uri "$Base/api/v1/auth/login" -ContentType 'application/json' -Body $body
$authHeaders = @{ Authorization = "Bearer $($owner.accessToken)" }
$body = @{ storeId=$StoreId; expiresInMinutes=30 } | ConvertTo-Json
$enrollment = Invoke-RestMethod -Method Post -Uri "$Base/api/v1/terminals/enrollment-token" -Headers $authHeaders -ContentType 'application/json' -Body $body
$fingerprint = 'solidpos-local-qa-' + [guid]::NewGuid().ToString('N')
$body = @{
  enrollmentToken=$enrollment.enrollmentToken
  name='Caja QA local'
  fingerprint=$fingerprint
  appVersion='AC-DEV-local-qa'
} | ConvertTo-Json
$terminal = Invoke-RestMethod -Method Post -Uri "$Base/api/v1/auth/terminal/register" -ContentType 'application/json' -Body $body
if (-not $terminal.accessToken -or -not $terminal.terminal.id -or -not $owner.user.id) {
    throw 'Faltan datos de la sesión local o del registro de terminal'
}
```

### Preparar SQLite del WPF comercial

La ruta es **fija en la implementación actual**. Si ya existe, **DETENERSE** y usar otro usuario Windows de QA o crear un respaldo SQLite consistente antes de continuar. Nunca copiar sólo `.sqlite` mientras esté abierto en WAL ni borrar bases ajenas.

```powershell
$Db = Join-Path $env:LOCALAPPDATA 'SolidBit\SolidPOS\terminal\poscore.sqlite'
if (Test-Path $Db) { throw "Ya existe una base local en $Db. No alterarla sin revisar/resguardar. Usar usuario Windows de QA independiente." }
New-Item -ItemType Directory -Force (Split-Path $Db -Parent) | Out-Null
$Cli = 'src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj'
dotnet run --project $Cli -- init --db $Db
dotnet run --project $Cli -- bind --db $Db --tenant-id $TenantId --store-id $StoreId --terminal-id $terminal.terminal.id --fingerprint $fingerprint --terminal-token $terminal.accessToken --schema-version 4
dotnet run --project $Cli -- sync-local-user --db $Db --tenant-id $TenantId --store-id $StoreId --user-id $owner.user.id --email $DevEmail --display-name 'Operador QA' --password $DevPassword --role supervisor --permissions 'sales.create,cash.open,cash.close,sales.void,sync.push,sync.pull'
dotnet run --project $Cli -- sync-catalog --db $Db --base-url $Base --access-token $terminal.accessToken
dotnet run --project $Cli -- catalog-status --db $Db --sku QSR-AMERICANO
dotnet run --project $Cli -- verify-local-integrity --db $Db
```

Se utilizan tokens y contraseña **sólo de desarrollo** en argumentos CLI, que podrían ser visibles a procesos locales. No usar credenciales reales ni pegar salidas con tokens en conversaciones o issues. Al terminar, limpiar variables en esa sesión de PowerShell y detener servicios de laboratorio.

No iniciar `sync-push` durante la primera prueba: la venta WPF se verifica primero en SQLite; un outbox **pendiente** no significa ACK remoto.

### Abrir y vender desde WPF

```powershell
dotnet run --project src/PosCore/SolidPOS.PosCore.Wpf/SolidPOS.PosCore.Wpf.csproj
```

1. Ir a **Activación / Estado real**, iniciar sesión con `owner@solidpos.local` / contraseña de desarrollo.
2. Abrir caja: **Fondo inicial (centavos MXN)** = `10000` (MX$100).
3. Buscar `QSR-AMERICANO` y **Agregar SKU**. Confirmar el precio actualizado; si es 4500 centavos, equivale a MX$45.
4. Capturar efectivo recibido = `5000` (MX$50), confirmar cobro. Esperar **VENTA GUARDADA en SQLite**, total `4500`, cambio `500`.
5. Confirmar efectivo esperado = `14500` (MX$145). Vaciar carrito si correspondiera.
6. **Efectivo contado físicamente** = `14500`; cerrar y conciliar turno. El cierre debe persistir.
7. Cerrar aplicación, abrirla nuevamente, volver a iniciar sesión y comprobar que se conservó el estado de caja y no aparece una segunda venta.

Si el precio del catálogo es distinto, **calcular importe esperado y cambio según precio real** en lugar de forzar cifras.

Impresión física y sync remoto todavía **no están validados en el flujo WPF actual**; el CLI contiene `FakeReceiptPrinter`. La existencia del print job no prueba entrega del papel.

## 3. Recoger evidencia y estado de salida

Registrar commit SHA, Windows/build, imágenes de interfaz sin secretos, resultados TRX, ticket y recibos sólo cuando existan realmente, resultado de arqueo, reinicios, incidencias y resultado PASS/FAIL/BLOCKED. Plantillas:

- `docs/engineering/SOLIDPOS_MVP_UAT_EVIDENCE_TEMPLATE.md`
- `docs/engineering/SOLIDPOS_MVP_UAT_EXECUTION.md`

**Nunca** usar `-ResetSchema` contra DB que contenga datos, ejecutar migraciones contra bases productivas, usar Railway/Supabase real en QA, ni marcar SYNC ACK sólo por tener outbox.

## 4. Decisión de proyecto

Se puede continuar desarrollando en `AC-DEV` **en paralelo**. La fase 1 (build/tests/self-test) y la fase 2 (WPF con SQLite de laboratorio) deben informar defectos P0/P1. No es necesario repetir la prueba histórica LGA-03 de tres días si el resultado confirmado cubre el mismo comportamiento; sólo revalidar regresiones de componentes modificados. Mantener NO-GO para venta a clientes hasta completar impresión, instalación, UAT de caja y recuperación y evidencia de sincronización según alcance.
