using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using SolidPOS.PosCore.Wpf.Composition;

namespace SolidPOS.PosCore.Wpf;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            Directory.CreateDirectory(".\\.runtime");
            var viewModel = PosCoreWpfCompositionRoot.CreateShellViewModel(".\\.runtime\\poscore-wpf-shell-self-test.sqlite");
            viewModel.InitializeForShell();

            var lines = new List<string>
            {
                "PosCore WPF self-test started.",
                "WPF shell initialized.",
                $"Local login view model ready: {viewModel.Login.Email}",
                $"Terminal status view model ready: {viewModel.TerminalStatus.Status}",
                $"Sales view model ready: {viewModel.Sales.CatalogSummary}",
                $"Sync status view model ready: {viewModel.SyncStatus.Status}",
                $"Cash shift view model ready: {viewModel.CashShift.Status}",
                "PosCore WPF shell validation completed."
            };

            File.WriteAllLines(".\\.runtime\\poscore-wpf-shell-self-test.log", lines);
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
