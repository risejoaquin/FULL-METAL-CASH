# SolidPOS — BETA-03 HOTFIX 03.1

## Motivo

El validador `scripts/beta/validate-beta-03-store-operations-validation.ps1` falló después de `dotnet build` y `dotnet test` por una unión inválida entre el parámetro PowerShell `-ContentType` y su valor `application/json`.

## Error observado

```text
No se encuentra ningún parámetro de posición que acepte el argumento '-ContentTypeapplication/json'.
```

## Corrección

Se corrigieron las dos llamadas HTTP afectadas:

```powershell
-ContentType 'application/json'
```

También se normalizó el body vacío de emisión de recibo:

```powershell
-Body '{}'
```

## Alcance

No se modificó lógica de dominio, persistencia, contratos HTTP, caja, ventas, pagos, recibos ni SQL de reconciliación. El hotfix corrige exclusivamente el binding de parámetros del validador BETA-03.

## Evidencia previa conservada

Los logs del usuario ya confirmaron:

- dotnet restore PASS
- dotnet build PASS — 0 warnings / 0 errors
- dotnet test PASS — 130/130

## Estado

`BETA-03 HOTFIX 03.1 — PENDING USER VALIDATION`
