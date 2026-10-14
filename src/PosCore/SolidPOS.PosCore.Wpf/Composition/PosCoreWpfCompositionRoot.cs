using System.IO;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Auth;
using SolidPOS.PosCore.Application.Startup;
using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Application.Cash;
using SolidPOS.PosCore.Infrastructure.SQLite;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Wpf.ViewModels;

namespace SolidPOS.PosCore.Wpf.Composition;

public static class PosCoreWpfCompositionRoot
{
    /// <summary>
    /// Commercial startup uses an application-owned, persistent SQLite path,
    /// never the current working directory or a demo database.
    /// </summary>
    public static string GetProductionDatabasePath()
    {
        string appData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            throw new InvalidOperationException("Windows local application data directory unavailable.");
        return Path.Combine(appData, "SolidBit", "SolidPOS", "terminal", "poscore.sqlite");
    }

    public static AppShellViewModel CreateProductionShellViewModel()
    {
        string databasePath = GetProductionDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var database = new SQLiteLocalDatabase(databasePath);
        var repository = new SQLiteLocalPosRepository(database);
        var clock = new SystemClock();
        // Prepare real dependencies for the next MVP unit. No fake session
        // or self-test path is introduced into commercial startup.
        var auth = new LocalAuthService(repository, clock);
        PosCoreStartupInspection state =
            new PosCoreStartupInspectionService(repository).InspectAsync()
                .GetAwaiter().GetResult();

        var shell = new AppShellViewModel(databasePath);
        shell.InitializeForProduction(state, auth, repository);
        if (shell.ProductionLogin is not null && shell.ProductionCart is not null)
        {
            var checkout = new AtomicCashCheckoutService(
                repository, new SQLiteAtomicCashCheckoutStore(database), clock);
            var cashClose = new AtomicCashShiftCloseService(
                new SQLiteAtomicCashShiftCloseStore(database), clock);
            shell.AttachProductionCheckout(new ProductionCheckoutViewModel(
                repository, auth, shell.ProductionLogin, shell.ProductionCart, checkout, cashClose, clock, new SQLiteCheckoutRecoveryJournal(database)));
        }
        return shell;
    }

    /// <summary>
    /// Explicit --self-test compatibility only. Must never be used as
    /// production shell because its commands change visual state only.
    /// </summary>
    public static AppShellViewModel CreateShellViewModel(string databasePath, TenantBrandingPackage? brandingPackage = null)
    {
        var viewModel = new AppShellViewModel(databasePath);
        if (brandingPackage is not null)
        {
            viewModel.ApplyBranding(brandingPackage);
        }
        viewModel.InitializeForShell();
        return viewModel;
    }
}
