# SolidPOS Iteration 08 Hotfix 08.1 — Catalog Cache Unit Test Compile Fix

## Problema

`CatalogCacheServiceTests.cs` usaba `[Fact]` pero no importaba `Xunit`, provocando error CS0246 en compilación.

## Corrección

Se agregó:

```csharp
using Xunit;
```

a `tests/SolidPOS.PosCore.UnitTests/CatalogCacheServiceTests.cs`.

## Validación esperada

```powershell
dotnet restore solidpos-platform.sln
dotnet build solidpos-platform.sln
dotnet test solidpos-platform.sln
```
