# SolidPOS Iteration 14 — PosCore WPF Shell + MVVM Foundation

## Status

Prepared for local validation.

## Objective

Create the real PosCore WPF shell over the already validated local runtime without placing business logic inside UI classes.

## Scope

- New WPF project: `SolidPOS.PosCore.Wpf`.
- MVVM foundation.
- Shell window.
- Login tab.
- Terminal status tab.
- Sales tab.
- Sync status tab.
- Cash shift tab.
- Composition root for ViewModels.
- Self-test mode for repeatable validation without manually opening the UI.

## Architectural rule

WPF is a presentation layer only.

```text
WPF Views
→ ViewModels
→ PosCore Application services
→ PosCore Infrastructure SQLite / hardware adapters
```

The shell does not own POS business rules, synchronization semantics, cash drawer logic, payment rules, inventory consumption, auth rules, or hardware implementation.

## Files added

```text
src/PosCore/SolidPOS.PosCore.Wpf/SolidPOS.PosCore.Wpf.csproj
src/PosCore/SolidPOS.PosCore.Wpf/App.xaml
src/PosCore/SolidPOS.PosCore.Wpf/App.xaml.cs
src/PosCore/SolidPOS.PosCore.Wpf/MainWindow.xaml
src/PosCore/SolidPOS.PosCore.Wpf/MainWindow.xaml.cs
src/PosCore/SolidPOS.PosCore.Wpf/Composition/PosCoreWpfCompositionRoot.cs
src/PosCore/SolidPOS.PosCore.Wpf/ViewModels/AppShellViewModel.cs
src/PosCore/SolidPOS.PosCore.Wpf/ViewModels/LoginViewModel.cs
src/PosCore/SolidPOS.PosCore.Wpf/ViewModels/TerminalStatusViewModel.cs
src/PosCore/SolidPOS.PosCore.Wpf/ViewModels/SalesViewModel.cs
src/PosCore/SolidPOS.PosCore.Wpf/ViewModels/SyncStatusViewModel.cs
src/PosCore/SolidPOS.PosCore.Wpf/ViewModels/CashShiftViewModel.cs
src/PosCore/SolidPOS.PosCore.Wpf/ViewModels/ViewModelBase.cs
src/PosCore/SolidPOS.PosCore.Wpf/ViewModels/RelayCommand.cs
scripts/poscore/validate-poscore-wpf-shell.ps1
ITERATION_14_VALIDATION_COMMANDS.md
```

## Validation

Run the commands from `ITERATION_14_VALIDATION_COMMANDS.md`.
