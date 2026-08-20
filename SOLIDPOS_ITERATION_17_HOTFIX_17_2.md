# SolidPOS Iteration 17 — Hotfix 17.2

## Issue

`validate-posbuilder-branding-package.ps1` still timed out at:

```text
Running PosBuilder WPF branding self-test...
```

Hotfix 17.1 added `Environment.Exit`, but the WPF app could still enter the generated WPF startup path before the self-test completed reliably.

## Fix

This hotfix adds an explicit process entry point for `SolidPOS.PosBuilder.Wpf`:

- `Program.Main(string[] args)` checks `--self-test` before creating any WPF `Application`.
- `PosBuilderSelfTestRunner.Run(args)` performs the branding self-test without starting Dispatcher/UI.
- The WPF app keeps normal visual startup when `--self-test` is not present.
- `SolidPOS.PosBuilder.Wpf.csproj` now sets `StartupObject` to `SolidPOS.PosBuilder.Wpf.Program`.
- `App.xaml.cs` only handles normal UI startup.

## Expected result

The self-test must finish under the script timeout and print:

```text
PosBuilder branding self-test started.
Builder shell initialized:
Tenant branding package generated:
Branding validation: isValid=True; errors=0; warnings=0
PosBuilder tenant branding package validation completed.
```
