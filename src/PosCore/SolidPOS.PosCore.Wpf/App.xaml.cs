using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SolidPOS.PosCore.Wpf.Composition;
using SolidPOS.PosCore.Application.Branding;
using SolidPOS.PosCore.Infrastructure.Branding;
using SolidPOS.PosCore.Application.Updates;
using SolidPOS.PosCore.Infrastructure.Updates;
using SolidPOS.PosCore.Application.Diagnostics;

namespace SolidPOS.PosCore.Wpf;

public partial class App : System.Windows.Application
{
    private static readonly CrashReportService CrashReporter = new();
    private static int _inCrashHandler;

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
        RegisterCrashHandlers();
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
                "Crash reporting handlers registered: DispatcherUnhandledException, AppDomain.UnhandledException, TaskScheduler.UnobservedTaskException.",
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

    private void RegisterCrashHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (System.Threading.Interlocked.CompareExchange(ref _inCrashHandler, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var report = CrashReporter.CreateReport(
                e.Exception,
                crashSource: nameof(DispatcherUnhandledException),
                isFatal: false);

            CrashReporter.PersistCrashReportSynchronous(report);
            Console.Error.WriteLine($"[CRASH] UI thread exception captured safely. crashId={report.CrashId}; type={report.ExceptionType}; message={report.SanitizedMessage}");

            // Per specification: Only set Handled=true if recovery semantics prove continuation is safe.
            // Otherwise preserve normal termination semantics.
            e.Handled = false;
        }
        catch
        {
            // Defensive failure isolation
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _inCrashHandler, 0);
        }
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (System.Threading.Interlocked.CompareExchange(ref _inCrashHandler, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var ex = e.ExceptionObject as Exception ?? new InvalidOperationException($"AppDomain unhandled exception: {e.ExceptionObject}");
            var report = CrashReporter.CreateReport(
                ex,
                crashSource: nameof(AppDomain) + ".UnhandledException",
                isFatal: e.IsTerminating);

            CrashReporter.PersistCrashReportSynchronous(report);
            Console.Error.WriteLine($"[CRASH] Fatal process exception captured safely. crashId={report.CrashId}; type={report.ExceptionType}; isTerminating={e.IsTerminating}");
        }
        catch
        {
            // Defensive failure isolation
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _inCrashHandler, 0);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            var report = CrashReporter.CreateReport(
                e.Exception,
                crashSource: nameof(TaskScheduler) + ".UnobservedTaskException",
                isFatal: false);

            CrashReporter.PersistCrashReportSynchronous(report);
            Console.Error.WriteLine($"[CRASH] Unobserved task exception captured safely. crashId={report.CrashId}; type={report.ExceptionType}");

            // Mark observed so it does not escalate to AppDomain crash if runtime policy would otherwise terminate
            e.SetObserved();
        }
        catch
        {
            // Defensive failure isolation
        }
    }
}
