# SolidPOS Iteration 15 Hotfix 15.1 — WPF SetProperty Return Contract

## Estado

Hotfix para error de compilación CS0029 en ViewModels WPF.

## Problema

`ViewModelBase.SetProperty` devolvía `void`, pero los ViewModels de Iteration 15 usan el patrón MVVM estándar:

```csharp
if (SetProperty(ref field, value))
{
    Recalculate();
}
```

Eso provocaba:

```text
CS0029: No se puede convertir implícitamente el tipo 'void' en 'bool'
```

## Corrección

`SetProperty<T>` ahora devuelve `bool`:

- `false` si el valor no cambió.
- `true` si actualizó el campo y disparó `PropertyChanged`.

## Archivo modificado

```text
src/PosCore/SolidPOS.PosCore.Wpf/ViewModels/ViewModelBase.cs
```

## Impacto

No cambia PosServer, SQLite, sync, auth, caja, hardware ni scripts. Solo corrige el contrato base MVVM del proyecto WPF.
