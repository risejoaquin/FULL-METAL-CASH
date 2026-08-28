# Macro Fase 33 Hotfix 33.1 — Observability compile fix

## Estado
IMPLEMENTED — pending local validation

## Problema
`PostgreSqlOperationalMetricsRepository.cs` fallaba al compilar porque usaba `IConfiguration.GetConnectionString(...)` sin importar el namespace de extensión `Microsoft.Extensions.Configuration`.

Error:

```text
CS1061: IConfiguration no contiene una definición para GetConnectionString
```

## Corrección
Se agregó `using Microsoft.Extensions.Configuration;` y se simplificó la firma del constructor a `IConfiguration`.

## Alcance
No cambia endpoints, contratos, OpenAPI, base de datos, ni lógica de negocio.

## Validación esperada

```powershell
dotnet restore solidpos-platform.sln
dotnet build solidpos-platform.sln
dotnet test solidpos-platform.sln
```

Esperado: build y tests PASS.
