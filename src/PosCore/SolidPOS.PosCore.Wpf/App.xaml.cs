using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using SolidPOS.PosCore.Wpf.Composition;
using SolidPOS.PosCore.Application.Branding;
using SolidPOS.PosCore.Infrastructure.Branding;
using SolidPOS.PosCore.Application.Updates;
using SolidPOS.PosCore.Infrastructure.Updates;

namespace SolidPOS.PosCore.Wpf;

public partial class App : System.Windows.Application
{
    private static string? GetOption(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        }
        return null;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            Directory.CreateDirectory(".\\.runtime");
            var brandingPackagePath = GetOption(e.Args, "--branding-package");
            SolidPOS.PosCore.Domain.TenantBrandingPackage? brandingPackage = null;
            if (!string.IsNullOrWhiteSpace(brandingPackagePath))
            {
                var brandingService = new TenantBrandingPackageService(new JsonTenantBrandingPackageStore());
                brandingPackage = brandingService.LoadValidatedAsync(brandingPackagePath).GetAwaiter().GetResult();
            }
            var updateManifestPath = GetOption(e.Args, "--update-manifest");
            SolidPOS.PosCore.Domain.UpdatePackageManifest? updateManifest = null;
            if (!string.IsNullOrWhiteSpace(updateManifestPath))
            {
                var updateService = new UpdatePackageManifestService(new JsonUpdatePackageManifestStore());
                updateManifest = updateService.LoadValidatedAsync(updateManifestPath).GetAwaiter().GetResult();
            }

            var viewModel = PosCoreWpfCompositionRoot.CreateShellViewModel(".\\.runtime\\poscore-wpf-sales-flow-self-test.sqlite", brandingPackage);
            viewModel.ExecuteQsrSelfTest();

            var lines = new List<string>
            {
                "PosCore WPF QSR self-test started.",
                "WPF shell initialized.",
                $"Branding package applied: tenantName={viewModel.Branding.TenantName}; appName={viewModel.Branding.AppName}; primaryColor={viewModel.Branding.PrimaryColorHex}; accentColor={viewModel.Branding.AccentColorHex}",
                $"Receipt branding ready: header={viewModel.Branding.ReceiptHeader}; footer={viewModel.Branding.ReceiptFooter}",
                updateManifest is null ? "Update manifest ready: none" : $"Update manifest ready: releaseVersion={updateManifest.ReleaseVersion}; channel={updateManifest.Channel}; packageFile={updateManifest.PackageFileName}; sha256={updateManifest.Sha256}",
                $"Local login view model ready: {viewModel.Login.Email}",
                $"Terminal status view model ready: {viewModel.TerminalStatus.Status}",
                $"Catalog view ready: {viewModel.Sales.CatalogSummary}",
                $"QSR cart ready: {viewModel.Sales.CartSummary}",
                $"Cash payment ready: {viewModel.Sales.PaymentSummary}",
                $"Receipt print flow ready: {viewModel.Sales.ReceiptStatus}; {viewModel.Sales.PrintStatus}",
                $"Sync visual state ready: {viewModel.SyncStatus.QueueSummary}",
                $"Cash shift view model ready: {viewModel.CashShift.ExpectedCashSummary}",
                $"QSR totals: totalCents={viewModel.Sales.TotalCents}; tenderedCents={viewModel.Sales.TenderedCents}; changeCents={viewModel.Sales.ChangeCents}; expectedCashCents={viewModel.CashShift.ExpectedCashCents}",
                "PosCore WPF sales flow QSR validation completed."
            };

            File.WriteAllLines(".\\.runtime\\poscore-wpf-sales-flow-qsr-self-test.log", lines);
            var shellCompatibilityLines = new List<string>
            {
                "PosCore WPF self-test started.",
                "WPF shell initialized.",
                $"Branding package applied: tenantName={viewModel.Branding.TenantName}; appName={viewModel.Branding.AppName}; primaryColor={viewModel.Branding.PrimaryColorHex}; accentColor={viewModel.Branding.AccentColorHex}",
                $"Receipt branding ready: header={viewModel.Branding.ReceiptHeader}; footer={viewModel.Branding.ReceiptFooter}",
                updateManifest is null ? "Update manifest ready: none" : $"Update manifest ready: releaseVersion={updateManifest.ReleaseVersion}; channel={updateManifest.Channel}; packageFile={updateManifest.PackageFileName}",
                $"Local login view model ready: {viewModel.Login.Email}",
                $"Terminal status view model ready: {viewModel.TerminalStatus.Status}",
                $"Sales view model ready: {viewModel.Sales.CatalogSummary}",
                $"Sync status view model ready: {viewModel.SyncStatus.Status}",
                $"Cash shift view model ready: {viewModel.CashShift.Status}",
                "PosCore WPF shell validation completed."
            };
            File.WriteAllLines(".\\.runtime\\poscore-wpf-shell-self-test.log", shellCompatibilityLines);
            foreach (var line in lines)
            {
                Console.WriteLine(line);
            }

            Shutdown(0);
            return;
        }

        var shell = new MainWindow
        {
            DataContext = PosCoreWpfCompositionRoot.CreateShellViewModel(".\\.runtime\\poscore-wpf.sqlite")
        };

        shell.Show();
    }
}
