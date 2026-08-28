# Macro Fase 29 Hotfix 29.1 — Unit Test Constructor Alignment

## Problema

`dotnet build` fallaba en `SyncEventProcessingServiceTests.cs` porque el constructor de
`SyncEventProcessingService` recibió el nuevo parámetro `ISyncConflictRepository`, pero el helper de test
`CreateService` no se actualizó.

## Corrección

Se actualizó el helper de pruebas para inyectar:

```csharp
Mock.Of<ISyncConflictRepository>()
```

entre `ISyncEventRepository` e `ISalesService`.

## Alcance

No cambia runtime, contratos, endpoints, migraciones ni reglas de negocio.

## Validación requerida

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```
