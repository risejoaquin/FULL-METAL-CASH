using SolidPOS.PosCore.Application.Recovery;
using SolidPOS.PosCore.Wpf.ViewModels;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class SalesViewModelRecoveryTests
{
    [Fact]
    public void Offline_state_is_visible_and_does_not_block_offline_sales()
    {
        var viewModel = new SalesViewModel();

        viewModel.SetConnectivity(PosCoreConnectivityState.Offline);

        Assert.Equal("OFFLINE", viewModel.ConnectivityState);
        Assert.Contains("Ventas offline siguen disponibles", viewModel.SyncVisualStatus);
        Assert.True(viewModel.AddAmericanoCommand.CanExecute(null));
    }

    [Fact]
    public void Online_recovery_updates_visible_state_without_restart()
    {
        var viewModel = new SalesViewModel();

        viewModel.SetConnectivity(PosCoreConnectivityState.Reconnecting);
        viewModel.SetConnectivity(PosCoreConnectivityState.Online);

        Assert.Equal("ONLINE", viewModel.ConnectivityState);
        Assert.Contains("Conexion restaurada", viewModel.SyncVisualStatus);
    }

    [Fact]
    public void Printer_failure_exposes_retry_print_not_sale_retry()
    {
        var viewModel = new SalesViewModel();

        viewModel.ShowPrinterFailure(new IOException("printer offline"));

        Assert.Contains("venta quedo guardada", viewModel.OperatorErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.RetryPrintCommand.CanExecute(null));
        Assert.False(viewModel.RetryDrawerCommand.CanExecute(null));
    }

    [Fact]
    public void Cash_drawer_failure_exposes_retry_drawer_without_duplicate_payment()
    {
        var viewModel = new SalesViewModel();

        viewModel.ShowCashDrawerFailure(new IOException("drawer offline"));

        Assert.Contains("pago quedo registrado", viewModel.OperatorErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.RetryDrawerCommand.CanExecute(null));
        Assert.False(viewModel.RetryPrintCommand.CanExecute(null));
    }

    [Fact]
    public void Supported_failure_can_be_dismissed_without_restart()
    {
        var viewModel = new SalesViewModel();

        viewModel.ShowOfflineFailure(new TimeoutException("network timeout"));
        Assert.True(viewModel.ContinueOfflineCommand.CanExecute(null));

        viewModel.DismissErrorCommand.Execute(null);

        Assert.Equal("Sin errores activos.", viewModel.OperatorErrorMessage);
        Assert.Equal("Acciones de recuperacion no requeridas.", viewModel.RecoveryActionsSummary);
    }
}
