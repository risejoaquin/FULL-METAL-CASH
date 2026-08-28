# SolidPOS Iteration 14 — Hotfix 14.1

## Objetivo

Corregir compilación WPF por colisión de nombre `Application`.

## Error corregido

```text
CS0118: 'Application' es espacio de nombres pero se usa como tipo
```

## Archivo afectado

```text
src/PosCore/SolidPOS.PosCore.Wpf/App.xaml.cs
```

## Cambio

```csharp
public partial class App : System.Windows.Application
```

## Impacto

No cambia runtime PosCore, SQLite, PosServer, sync, auth, caja, hardware ni contratos. Es un fix de namespace para compilación del proyecto WPF.
