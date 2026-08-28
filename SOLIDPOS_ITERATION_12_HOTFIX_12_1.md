# SolidPOS Iteration 12 Hotfix 12.1 — xUnit Analyzer Compliance

## Problema

`dotnet build` falla por `xUnit2031` en `tests/SolidPOS.PosCore.UnitTests/LocalAuthServiceTests.cs`.

El test usaba:

```csharp
Assert.Single(repository.AuditEvents.Where(x => x.EventType == "local.auth.blocked_offline_window"));
```

Con los analizadores de xUnit activos, esa forma se trata como error.

## Corrección

Se reemplazó por el overload recomendado:

```csharp
Assert.Single(repository.AuditEvents, x => x.EventType == "local.auth.blocked_offline_window");
```

## Alcance

No cambia runtime, SQLite, auth local, RBAC, sync, PosServer ni scripts. Solo corrige el test para cumplir el analyzer `xUnit2031`.

## Validación

Ejecutar:

```powershell
dotnet restore solidpos-platform.sln
dotnet build solidpos-platform.sln
dotnet test solidpos-platform.sln
```

Luego continuar con `scripts\poscore\validate-poscore-offline-auth-rbac.ps1`.
