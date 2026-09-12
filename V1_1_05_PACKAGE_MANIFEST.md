# V1.1-05 Package Manifest

phase: V1.1-05 PosCore Error UX & Recovery
baseline: 125d728067d9fd5357428c2111f21c88a76ab37a
schemaVersion: 4
syncContract: schema_version_4

## Files

- SOLIDPOS_V1_1_05_POSCORE_ERROR_UX_RECOVERY.md
- V1_1_05_PACKAGE_MANIFEST.md
- V1_1_05_VALIDATION_COMMANDS.md
- scripts/v1.1/validate-v1.1-05-poscore-error-ux-recovery.ps1
- src/PosCore/SolidPOS.PosCore.Application/Recovery/OperatorRecoveryModels.cs
- src/PosCore/SolidPOS.PosCore.Application/Recovery/OperatorRecoveryService.cs
- src/PosCore/SolidPOS.PosCore.Wpf/ViewModels/SalesViewModel.cs
- src/PosCore/SolidPOS.PosCore.Wpf/MainWindow.xaml
- tests/SolidPOS.PosCore.UnitTests/OperatorRecoveryServiceTests.cs
- tests/SolidPOS.PosCore.UnitTests/SalesViewModelRecoveryTests.cs
- tests/SolidPOS.PosCore.UnitTests/SolidPOS.PosCore.UnitTests.csproj
- .github/workflows/solidpos-ci.yml

## Constraints

- No PosServer production behavior changes.
- No database migration changes.
- No package dependency additions.
- No sync contract changes.
- No Railway deployment required unless a future backend change is explicitly authorized.
